using MediatR;
using Citationly.Application.Dtos;
using Citationly.Application.Helpers;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Features.Competitors;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.GeoDashboard;

/// <summary>
/// Fans out to all GEO-dashboard services concurrently and assembles the full response DTO.
/// Derives weakestPillarInsight and opportunityInsight in-memory (no extra DB calls).
/// </summary>
public class GeoDashboardAggregator
{
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IGeoPillarService _pillarService;
    private readonly IActivityFeedService _activityService;
    private readonly IEngineScanService _engineScanService;
    private readonly IGeoScoreEvidenceService _scoreEvidenceService;
    private readonly IMediator _mediator;

    public GeoDashboardAggregator(
        IAiVisibilityRepository visibilityRepo,
        IGeoPillarService pillarService,
        IActivityFeedService activityService,
        IEngineScanService engineScanService,
        IGeoScoreEvidenceService scoreEvidenceService,
        IMediator mediator)
    {
        _visibilityRepo = visibilityRepo;
        _pillarService = pillarService;
        _activityService = activityService;
        _engineScanService = engineScanService;
        _scoreEvidenceService = scoreEvidenceService;
        _mediator = mediator;
    }

    public async Task<GeoDashboardDto> BuildAsync(Guid organizationId, string range)
    {
        // First-ever visit for this org: no scan has run yet. Try to bootstrap real data
        // from whatever onboarding analysis already exists (persona/region/executive summary,
        // website profile, competitors) instead of showing fabricated numbers.
        var initialScans = await _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId);
        if (!initialScans.Any(IsUsableScan))
        {
            await _mediator.Send(new RunScanCommand { OrganizationId = organizationId });
        }

        // ── Fan-out: fire all data fetches concurrently ─────────────
        var scansTask      = _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId);
        var sovTask         = _visibilityRepo.GetShareOfVoiceByOrgAsync(organizationId);
        var competitorsTask = _visibilityRepo.GetCompetitorsByOrgAsync(organizationId);
        var pillarsTask     = _pillarService.GetPillarsAsync(organizationId, range);
        var activityTask    = _activityService.GetRecentEventsAsync(organizationId);
        var engineTask      = _engineScanService.GetScanStatsAsync(organizationId);
        var evidenceTask    = _scoreEvidenceService.GetAsync(organizationId, DateTime.UtcNow.AddDays(-30));

        await Task.WhenAll(scansTask, sovTask, competitorsTask, pillarsTask, activityTask, engineTask, evidenceTask);

        var scans       = scansTask.Result.Where(IsUsableScan).ToList();
        var sovDb       = sovTask.Result;
        var competitors = competitorsTask.Result;
        var pillars     = pillarsTask.Result;
        var activity    = activityTask.Result;
        var (enginesScanned, promptsTracked) = engineTask.Result;
        var evidence     = evidenceTask.Result;

        // ── Scores ────────────────────────────────────────────────────
        var latestScan   = scans.LastOrDefault();
        var previousScan = scans.Count > 1 ? scans[scans.Count - 2] : null;
        var hasData      = latestScan != null;

        ScoreCardDto scores;

        if (latestScan == null)
        {
            // No scan exists (the org has no onboarding analysis to bootstrap from either) —
            // an honest zeroed-out state rather than fabricated numbers.
            var empty = Unavailable("No completed scan or measured evidence is available.");
            scores = new ScoreCardDto(empty, empty, empty, empty, empty, empty, empty, empty);
        }
        else
        {
            var isLegacyAiScan = latestScan.ScoringMethodVersion == "v1-ai-generated";
            var hasTechnicalAudit = latestScan.ScoringMethodVersion is "v3-geo-audit" or "v4-evidence-audit";
            var visibility = isLegacyAiScan || evidence.AnalysisCount == 0
                ? Unavailable("No measured prompt analyses are available for this score.")
                : Measured(latestScan.VisibilityScore, previousScan?.VisibilityScore, "derived", "Captured AI responses", "prompt-visibility:v5-search-grounded-sampled", null, null, evidence.AnalysisCount,
                    $"Average brand mention coverage across {evidence.AnalysisCount} prompt analyses in the 30-day scoring window.");
            var citation = isLegacyAiScan || evidence.AnalysisCount == 0
                ? Unavailable("No citation-eligible prompt analyses are available.")
                : Measured(latestScan.CitationScore, previousScan?.CitationScore,
                    evidence.OwnedCitationAnalysisCount == 0 ? "observed-zero" : "derived",
                    "Extracted response citations", "owned-citation-analysis-coverage:v1",
                    evidence.OwnedCitationAnalysisCount, evidence.AnalysisCount, evidence.AnalysisCount,
                    $"{evidence.OwnedCitationAnalysisCount} of {evidence.AnalysisCount} analyzed prompts contained a citation to the owned domain.");
            var sentiment = isLegacyAiScan || evidence.ClassifiedResponseCount == 0
                ? Unavailable("No response-level sentiment classifications are available.")
                : Measured(latestScan.SentimentScore, previousScan?.SentimentScore, "derived", "Captured AI responses", "net-sentiment:v1", null, null, evidence.ClassifiedResponseCount,
                    $"Calculated from {evidence.ClassifiedResponseCount} classified AI responses; classification may use an AI judge when deterministic rules are inconclusive.");
            var competitor = isLegacyAiScan || evidence.CompetitorResponseCount < RunCompetitorScanCommandHandler.MinimumResponseCount || evidence.RankedBrandCount < 2
                ? Unavailable($"A rank requires at least {RunCompetitorScanCommandHandler.MinimumResponseCount} responses and two evidence-validated brands. Current evidence: {evidence.CompetitorResponseCount} responses, {evidence.RankedBrandCount} ranked brands.", "insufficient-evidence")
                : Measured(latestScan.CompetitorScore, previousScan?.CompetitorScore, "derived", "Evidence-validated OpenAI competitor benchmark", CompetitorEvidenceScorer.MethodologyVersion, null, null, evidence.CompetitorResponseCount,
                    $"Relative percentile across {evidence.RankedBrandCount} evidence-validated brands from {evidence.CompetitorResponseCount} OpenAI responses; not a market-wide rank.");

            scores = new ScoreCardDto(
                visibility,
                citation,
                sentiment,
                competitor,
                Unavailable("No claim-level fact-checking monitor exists yet, so an honest hallucination-risk score cannot be reported."),
                hasTechnicalAudit
                    ? Measured(latestScan.SeoHealth, previousScan?.SeoHealth, "audited", "Deterministic website audit", "geo-technical-audit:v1", null, null, null, "Weighted audit of crawler access, sitemap, canonical, headings, metadata, and server-rendered content.")
                    : Unavailable("A deterministic website audit has not been completed for this scan."),
                hasTechnicalAudit
                    ? Measured(latestScan.AeoReadiness, previousScan?.AeoReadiness, "audited", "Deterministic website audit", "geo-technical-audit:v1", null, null, null, "Weighted audit of answer structure, schema coverage, extractability, and entity clarity.")
                    : Unavailable("A deterministic website audit has not been completed for this scan."),
                hasTechnicalAudit
                    ? Measured(latestScan.GeoReadiness, previousScan?.GeoReadiness, "audited", "Deterministic website audit", "geo-technical-audit:v1", null, null, null, "Weighted technical GEO audit; no AI-generated value is used for this score.")
                    : Unavailable("A deterministic website audit has not been completed for this scan."));
        }

        var latestHasVerifiedAudit = latestScan?.ScoringMethodVersion is "v3-geo-audit" or "v4-evidence-audit";
        var verifiedPillars = latestHasVerifiedAudit ? pillars : new List<GeoPillarDto>();
        var verifiedCoverage = new List<PromptTypeCoverageDto>();

        // ── Trend ─────────────────────────────────────────────────────
        var trend = scans
            .Select((s, idx) => new TrendPointDto(idx + 1, s.VisibilityScore))
            .ToList();

        // ── Share of voice ────────────────────────────────────────────
        List<ShareOfVoiceEntryDto> shareOfVoice;
        var usableScanDates = scans.Select(s => s.ScanDate).ToHashSet();
        var usableSovRows = sovDb
            .Where(s => s.SharePercentage > 0 && usableScanDates.Contains(s.ScanDate))
            .ToList();
        var latestScanDate = usableSovRows.OrderByDescending(s => s.ScanDate).FirstOrDefault()?.ScanDate;

        shareOfVoice = latestScanDate != null
            ? usableSovRows.Where(s => s.ScanDate == latestScanDate)
                   .Select(s => new ShareOfVoiceEntryDto(s.CompetitorName, s.SharePercentage, s.ColorCode))
                   .ToList()
            : new List<ShareOfVoiceEntryDto>();

        // ── Header (composite from scorecard) ───────────────────────
        var compositeValues = new[]
        {
            scores.VisibilityScore.Value,
            scores.CitationScore.Value,
            scores.SentimentScore.Value,
            scores.CompetitorScore.Value,
            scores.SeoHealth.Value,
            scores.AeoReadiness.Value,
            scores.GeoReadiness.Value
        }.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        int? compositeScore = compositeValues.Count == 0
            ? null
            : (int)Math.Round(compositeValues.Average());

        // For composite change, use the GeoReadiness change as a proxy (it represents overall GEO)
        var compositeChange = scores.GeoReadiness.Change;

        // No licensed or cross-tenant industry benchmark exists. Do not relabel tracked competitor
        // authority as an industry average.
        int? industryAverage = null;
        int? deltaVsIndustry = null;

        var header = new GeoDashboardHeaderDto(
            CompositeScore:  compositeScore,
            Grade:           compositeScore.HasValue ? GradeCalculator.ToGrade(compositeScore.Value) : "N/A",
            IndustryAverage: industryAverage,
            DeltaVsIndustry: deltaVsIndustry,
            CompositeChange: compositeChange,
            EnginesScanned:  enginesScanned,
            PromptsTracked:  promptsTracked,
            Status:          hasData ? "live" : "pending",
            ScoringMethodVersion: latestScan?.ScoringMethodVersion ?? "unavailable");

        // ── Weakest pillar insight (derived) ────────────────────────
        WeakestPillarInsightDto? weakestInsight = null;
        if (verifiedPillars.Count > 0)
        {
            var weakest = verifiedPillars.MinBy(p => p.Score)!;
            weakestInsight = new WeakestPillarInsightDto(
                PillarKey: weakest.Key,
                Score:     weakest.Score,
                Message:   "Audit your key pages to find exactly what to fix.",
                CtaLabel:  "Run Page auditor",
                CtaLink:   "/geo-engine/page-auditor");
        }

        // ── Opportunity insight (derived) ───────────────────────────
        OpportunityInsightDto? opportunityInsight = null;
        if (verifiedCoverage.Count > 0)
        {
            var lowestCoverage = verifiedCoverage.MinBy(c => c.Percentage)!;
            opportunityInsight = new OpportunityInsightDto(
                Message:  $"{lowestCoverage.Type} prompts ({lowestCoverage.Percentage}%) are your biggest untapped surface — turn them into prioritized missions.",
                CtaLabel: "Open Opportunity Finder",
                CtaLink:  "/opportunity-finder");
        }

        // ── Verify insight (static) ─────────────────────────────────
        var verifyInsight = new VerifyInsightDto(
            Message:  "Verify any of these answers live before acting on them.",
            CtaLabel: "Test in Answer simulator",
            CtaLink:  "/geo-engine/answer-simulator");

        // ── Assemble ────────────────────────────────────────────────
        return new GeoDashboardDto(
            HasData:              hasData,
            Scores:               scores,
            Trend:                trend,
            ShareOfVoice:         shareOfVoice,
            Header:               header,
            Pillars:              verifiedPillars,
            WeakestPillarInsight: weakestInsight,
            PromptTypeCoverage:   verifiedCoverage,
            OpportunityInsight:   opportunityInsight,
            WinsAndLosses:        activity,
            VerifyInsight:        verifyInsight);
    }

    // ── Private helpers (moved from controller) ─────────────────────
    private static string? GetChangeStr(int current, int? prev)
    {
        if (prev is null) return null;
        if (prev == 0) return current == 0 ? "0 pts" : $"+{current} pts";
        var diff = current - prev.Value;
        var pct = Math.Round((decimal)diff / prev.Value * 100, 1);
        return pct >= 0 ? $"+{pct}%" : $"{pct}%";
    }

    private static ScoreEntryDto Measured(
        int value,
        int? previous,
        string status,
        string source,
        string methodology,
        int? numerator,
        int? denominator,
        int? sampleSize,
        string evidence) => new(
            value,
            GetChangeStr(value, previous),
            GetDirection(value, previous),
            status,
            source,
            methodology,
            numerator,
            denominator,
            sampleSize,
            evidence);

    private static ScoreEntryDto Unavailable(string evidence, string status = "no-data") =>
        new(null, null, "flat", status, "No verified source", "unavailable", null, null, null, evidence);

    private static string GetDirection(int current, int? prev)
    {
        if (prev == null || current == prev.Value) return "flat";
        return current > prev.Value ? "up" : "down";
    }

    private static bool IsUsableScan(HistoricalScan scan)
    {
        if (scan.ScoringMethodVersion.StartsWith("v4-evidence-", StringComparison.Ordinal)) return true;
        return new[]
        {
            scan.VisibilityScore,
            scan.CitationScore,
            scan.SentimentScore,
            scan.CompetitorScore,
            scan.HallucinationRisk,
            scan.SeoHealth,
            scan.AeoReadiness,
            scan.GeoReadiness
        }.Any(score => score > 0);
    }
}
