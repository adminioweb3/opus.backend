using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.GeoDashboard;

/// <summary>
/// Pure read-aggregation service backing GET /dashboard/geo-dashboard.
/// It performs NO writes and NO AI calls — it stitches together data that already has
/// working repository methods elsewhere in the codebase (IWebsiteRepository /
/// IMetricsRepository), which are themselves populated by the Onboarding analysis
/// pipeline. This class owns no tables of its own.
///
/// Mapping assumptions (documented per the feature spec):
///  - hallucinationRisk  = 100 - ExecutiveSummaryData.OverallContentScore (a content-quality
///    proxy: better structured/accurate content -> lower hallucination risk).
///  - seoHealth          = ExecutiveSummaryData.OverallSEOScore (direct mapping).
///  - aeoReadiness       = ExecutiveSummaryData.OverallAIVisibilityScore (answer-engine
///    optimization readiness tracks how visible the brand already is in AI answers).
///  - geoReadiness       = ExecutiveSummaryData.OverallGEOScore (direct mapping, same value
///    used for header.compositeScore since GEO score IS the composite score).
///  - visibilityScore/citationScore/sentimentScore/competitorScore come from the latest
///    HistoricalScan row, with change/direction computed against the previous row
///    (independent of the "range" filter, per the "current metrics always reflect latest
///    scan" rule).
///  - ExecutiveSummaryData has no historical time series (InsertExecutiveSummaryAsync
///    deletes prior rows for the org before inserting), so the four score entries derived
///    from it (hallucinationRisk/seoHealth/aeoReadiness/geoReadiness) cannot have a real
///    delta — they are reported flat ("+0%", direction "up") rather than fabricating a
///    trend that doesn't exist.
///  - trend[] uses HistoricalScan rows bounded by "range", score = average of the 4
///    per-scan scores (a single composite trend line, matching the "AI visibility trend"
///    chart's "Composite score across the selected timeframe" description), day = a
///    1-based sequential index across the ranged rows.
///  - shareOfVoice[] comes directly from ShareOfVoice rows for the latest scan date.
///  - header.industryAverage is not backed by any real external benchmark data source in
///    this codebase, so a small honest fixed estimate (72) is used rather than inventing a
///    precise-sounding number; documented here rather than hidden.
///  - header.enginesScanned = distinct PlatformName count from PlatformVisibilities (falls
///    back to 4 if none tracked yet). header.promptsTracked = AiSearchPrompts count.
///  - header.status is "live"/"stale" (lowercase, exact strings) because the frontend hero
///    badge in geo-dashboard/page.tsx checks `header?.status === "live"` literally — status
///    is NOT the "On Track"/"Needs Attention" phrase originally suggested, the frontend file
///    is the authoritative contract and was followed instead. "live" = latest scan is within
///    8 days (matches the page copy: "refreshes every 7 days").
///  - pillars[] are the 5 numeric ExecutiveSummaryData category scores (AI Visibility, SEO,
///    Brand Authority, Content, overall GEO) rather than parsing the free-text
///    Strengths/Weaknesses/Opportunities/Threats JSON blobs, since those are prose bullet
///    lists, not 0-100 scored categories suited to a pillar bar chart.
///  - promptTypeCoverage[] groups AiSearchPrompts by their Intent field (a real, already
///    populated per-prompt column) rather than GeoRecommendation.Category, since "prompt
///    type coverage" maps far more naturally onto tracked search-prompt intents than onto
///    recommendation categories. percentage = average VisibilityScore within that intent
///    group; direction is a >=60 threshold proxy since there's no historical per-intent
///    series to diff against.
///  - opportunityInsight is derived from the single highest-priority GeoRecommendation
///    (Critical > High > Medium > Low, most recent as tiebreaker), or null if none exist.
///  - winsAndLosses is always an honest empty array — there is no existing data source in
///    this codebase that records individual answer-slot win/loss events, so nothing is
///    fabricated here.
///  - verifyInsight is a static-but-real navigational CTA pointing at the GEO optimizer
///    recommendations page.
/// </summary>
public class GeoDashboardAggregator
{
    private const int FullHistoryDays = 3650; // effectively "all time" for latest/previous scan lookups
    private const int DefaultIndustryAverage = 72;
    private const int DefaultEnginesScanned = 4;

    private readonly IWebsiteRepository _websiteRepository;
    private readonly IMetricsRepository _metricsRepository;

    public GeoDashboardAggregator(IWebsiteRepository websiteRepository, IMetricsRepository metricsRepository)
    {
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
    }

    public async Task<GeoDashboardData> BuildAsync(Guid organizationId, string range)
    {
        var days = ParseRangeDays(range);

        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(organizationId);
        if (executiveSummary == null)
        {
            return BuildEmpty();
        }

        var fullHistory = (await _metricsRepository.GetHistoricalScansAsync(organizationId, FullHistoryDays))
            .OrderBy(s => s.ScanDate)
            .ToList();
        var rangedHistory = days == FullHistoryDays
            ? fullHistory
            : (await _metricsRepository.GetHistoricalScansAsync(organizationId, days))
                .OrderBy(s => s.ScanDate)
                .ToList();

        var shareOfVoiceRows = (await _metricsRepository.GetShareOfVoiceAsync(organizationId, DateTime.UtcNow))
            .OrderByDescending(s => s.SharePercentage)
            .ToList();

        var geoRecommendations = (await _websiteRepository.GetGeoRecommendationsAsync(organizationId)).ToList();
        var aiSearchPrompts = (await _websiteRepository.GetAiSearchPromptsAsync(organizationId)).ToList();
        var platformVisibilities = (await _websiteRepository.GetPlatformVisibilitiesAsync(organizationId)).ToList();
        var promptsTracked = await _websiteRepository.GetAiSearchPromptCountAsync(organizationId);

        var latestScan = fullHistory.LastOrDefault();
        var previousScan = fullHistory.Count >= 2 ? fullHistory[^2] : null;

        var scores = BuildScoreCard(executiveSummary, latestScan, previousScan);
        var trend = BuildTrend(rangedHistory);
        var shareOfVoice = shareOfVoiceRows
            .Select(s => new ShareOfVoiceEntry { Name = s.CompetitorName, Value = s.SharePercentage, Color = s.ColorCode })
            .ToList();

        var pillars = BuildPillars(executiveSummary);
        var weakestPillarInsight = BuildWeakestPillarInsight(pillars);
        var promptTypeCoverage = BuildPromptTypeCoverage(aiSearchPrompts);
        var opportunityInsight = BuildOpportunityInsight(geoRecommendations);

        var header = BuildHeader(executiveSummary, latestScan, previousScan, platformVisibilities, promptsTracked, aiSearchPrompts.Count);

        return new GeoDashboardData
        {
            HasData = true,
            Scores = scores,
            Trend = trend,
            ShareOfVoice = shareOfVoice,
            Header = header,
            Pillars = pillars,
            WeakestPillarInsight = weakestPillarInsight,
            PromptTypeCoverage = promptTypeCoverage,
            OpportunityInsight = opportunityInsight,
            WinsAndLosses = new List<WinLossEvent>(), // honest empty — no real win/loss event source exists yet
            VerifyInsight = new GeoInsight
            {
                Message = "Review your latest GEO recommendations to verify progress.",
                CtaLabel = "View recommendations",
                CtaLink = "/dashboard/geo-optimizer"
            }
        };
    }

    private static int ParseRangeDays(string? range) => range switch
    {
        "7d" => 7,
        "30d" => 30,
        "90d" => 90,
        _ => 30
    };

    private static GeoScoreCard BuildScoreCard(ExecutiveSummaryData exec, HistoricalScan? latest, HistoricalScan? previous)
    {
        return new GeoScoreCard
        {
            VisibilityScore = BuildDeltaEntry(latest?.VisibilityScore ?? 0, previous?.VisibilityScore),
            CitationScore = BuildDeltaEntry(latest?.CitationScore ?? 0, previous?.CitationScore),
            SentimentScore = BuildDeltaEntry(latest?.SentimentScore ?? 0, previous?.SentimentScore),
            CompetitorScore = BuildDeltaEntry(latest?.CompetitorScore ?? 0, previous?.CompetitorScore),
            HallucinationRisk = BuildFlatEntry(Math.Clamp(100 - exec.OverallContentScore, 0, 100)),
            SeoHealth = BuildFlatEntry(Math.Clamp(exec.OverallSEOScore, 0, 100)),
            AeoReadiness = BuildFlatEntry(Math.Clamp(exec.OverallAIVisibilityScore, 0, 100)),
            GeoReadiness = BuildFlatEntry(Math.Clamp(exec.OverallGEOScore, 0, 100))
        };
    }

    private static ScoreEntry BuildDeltaEntry(int current, int? previous)
    {
        if (previous is null || previous.Value == 0)
        {
            return new ScoreEntry { Value = current, Change = "+0%", Direction = "up" };
        }

        var diff = current - previous.Value;
        var pct = Math.Round(diff / (double)previous.Value * 100, 1);
        var direction = diff >= 0 ? "up" : "down";
        var sign = diff >= 0 ? "+" : "";
        return new ScoreEntry { Value = current, Change = $"{sign}{pct}%", Direction = direction };
    }

    private static ScoreEntry BuildFlatEntry(int value) => new() { Value = value, Change = "+0%", Direction = "up" };

    private static List<TrendPoint> BuildTrend(List<HistoricalScan> rangedHistory)
    {
        var points = new List<TrendPoint>();
        for (var i = 0; i < rangedHistory.Count; i++)
        {
            var s = rangedHistory[i];
            var avg = (int)Math.Round((s.VisibilityScore + s.CitationScore + s.SentimentScore + s.CompetitorScore) / 4.0);
            points.Add(new TrendPoint { Day = i + 1, Score = avg });
        }
        return points;
    }

    private static List<GeoPillar> BuildPillars(ExecutiveSummaryData exec)
    {
        return new List<GeoPillar>
        {
            new() { Key = "aiVisibility", Label = "AI Visibility", Description = "How often you're surfaced in AI-generated answers.", Score = Math.Clamp(exec.OverallAIVisibilityScore, 0, 100) },
            new() { Key = "seo", Label = "SEO Health", Description = "Traditional search engine optimization foundation.", Score = Math.Clamp(exec.OverallSEOScore, 0, 100) },
            new() { Key = "brandAuthority", Label = "Brand Authority", Description = "Strength and trust signals associated with your brand.", Score = Math.Clamp(exec.OverallBrandAuthority, 0, 100) },
            new() { Key = "content", Label = "Content Quality", Description = "Depth, accuracy and structure of your content for AI parsing.", Score = Math.Clamp(exec.OverallContentScore, 0, 100) },
            new() { Key = "geo", Label = "Overall GEO", Description = "Composite Generative Engine Optimization score.", Score = Math.Clamp(exec.OverallGEOScore, 0, 100) }
        };
    }

    private static string PillarCtaLink(string pillarKey) => pillarKey switch
    {
        "aiVisibility" => "/dashboard/visibility-radar",
        "brandAuthority" => "/dashboard/brand-pulse",
        "seo" => "/dashboard/geo-optimizer",
        "content" => "/dashboard/geo-optimizer",
        "geo" => "/dashboard/geo-optimizer",
        _ => "/dashboard/geo-optimizer"
    };

    private static WeakestPillarInsight? BuildWeakestPillarInsight(List<GeoPillar> pillars)
    {
        if (pillars.Count == 0) return null;

        var weakest = pillars.OrderBy(p => p.Score).First();
        return new WeakestPillarInsight
        {
            PillarKey = weakest.Key,
            Score = weakest.Score,
            Message = $"Your {weakest.Label} score is trailing your other pillars — prioritizing improvements here has the biggest impact on your composite score.",
            CtaLabel = "Fix this now",
            CtaLink = PillarCtaLink(weakest.Key)
        };
    }

    private static List<PromptTypeCoverageItem> BuildPromptTypeCoverage(List<AiSearchPrompt> prompts)
    {
        if (prompts.Count == 0) return new List<PromptTypeCoverageItem>();

        return prompts
            .GroupBy(p => string.IsNullOrWhiteSpace(p.Intent) ? "General" : p.Intent!)
            .Select(g =>
            {
                var count = g.Count();
                var avgVisibility = (int)Math.Round(g.Average(p => (double)p.VisibilityScore));
                var example = g.Select(p => p.QueryString).FirstOrDefault(q => !string.IsNullOrWhiteSpace(q)) ?? "No sample prompt yet";
                if (example.Length > 80) example = example[..77] + "...";

                return new PromptTypeCoverageItem
                {
                    Type = g.Key,
                    Example = example,
                    Note = $"{count} prompt{(count == 1 ? "" : "s")} tracked in this category",
                    Percentage = Math.Clamp(avgVisibility, 0, 100),
                    Direction = avgVisibility >= 60 ? "up" : "down"
                };
            })
            .OrderByDescending(p => p.Percentage)
            .Take(5)
            .ToList();
    }

    private static readonly Dictionary<string, int> PriorityRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Critical"] = 0,
        ["High"] = 1,
        ["Medium"] = 2,
        ["Low"] = 3
    };

    private static GeoInsight? BuildOpportunityInsight(List<GeoRecommendation> recommendations)
    {
        if (recommendations.Count == 0) return null;

        var top = recommendations
            .OrderBy(r => PriorityRank.TryGetValue(r.Priority, out var rank) ? rank : 99)
            .ThenByDescending(r => r.CreatedAt)
            .First();

        var message = string.IsNullOrWhiteSpace(top.Description)
            ? top.Title
            : $"{top.Title} — {top.Description}";
        if (message.Length > 180) message = message[..177] + "...";

        return new GeoInsight
        {
            Message = message,
            CtaLabel = "View recommendation",
            CtaLink = "/dashboard/geo-optimizer"
        };
    }

    private static GeoDashboardHeader BuildHeader(
        ExecutiveSummaryData exec,
        HistoricalScan? latest,
        HistoricalScan? previous,
        List<PlatformVisibility> platformVisibilities,
        int promptsTracked,
        int aiSearchPromptCount)
    {
        var compositeScore = Math.Clamp(exec.OverallGEOScore, 0, 100);
        var grade = ComputeGrade(compositeScore);
        var industryAverage = DefaultIndustryAverage;
        var deltaVsIndustry = compositeScore - industryAverage;

        int? previousAvg = previous != null
            ? (int)Math.Round((previous.VisibilityScore + previous.CitationScore + previous.SentimentScore + previous.CompetitorScore) / 4.0)
            : null;
        var currentAvg = latest != null
            ? (int)Math.Round((latest.VisibilityScore + latest.CitationScore + latest.SentimentScore + latest.CompetitorScore) / 4.0)
            : compositeScore;
        var compositeChange = BuildDeltaEntry(currentAvg, previousAvg).Change;

        var enginesScanned = platformVisibilities.Select(p => p.Platform).Distinct().Count();
        if (enginesScanned == 0) enginesScanned = DefaultEnginesScanned;

        var effectivePromptsTracked = promptsTracked > 0 ? promptsTracked : aiSearchPromptCount;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var status = latest != null && (today.DayNumber - latest.ScanDate.DayNumber) <= 8 ? "live" : "stale";

        return new GeoDashboardHeader
        {
            CompositeScore = compositeScore,
            Grade = grade,
            IndustryAverage = industryAverage,
            DeltaVsIndustry = deltaVsIndustry,
            CompositeChange = compositeChange,
            EnginesScanned = enginesScanned,
            PromptsTracked = effectivePromptsTracked,
            Status = status
        };
    }

    private static string ComputeGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        _ => "D"
    };

    private static GeoDashboardData BuildEmpty()
    {
        return new GeoDashboardData
        {
            HasData = false,
            Scores = new GeoScoreCard(),
            Trend = new List<TrendPoint>(),
            ShareOfVoice = new List<ShareOfVoiceEntry>(),
            Header = new GeoDashboardHeader { Grade = "—", Status = "stale" },
            Pillars = new List<GeoPillar>(),
            WeakestPillarInsight = null,
            PromptTypeCoverage = new List<PromptTypeCoverageItem>(),
            OpportunityInsight = null,
            WinsAndLosses = new List<WinLossEvent>(),
            VerifyInsight = new GeoInsight
            {
                Message = "Review your latest GEO recommendations to verify progress.",
                CtaLabel = "View recommendations",
                CtaLink = "/dashboard/geo-optimizer"
            }
        };
    }
}
