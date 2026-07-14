using MediatR;
using Citationly.Application.Dtos;
using Citationly.Application.Helpers;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;

namespace Citationly.Application.Features.GeoDashboard;

/// <summary>
/// Fans out to all GEO-dashboard services concurrently and assembles the full response DTO.
/// Derives weakestPillarInsight and opportunityInsight in-memory (no extra DB calls).
/// </summary>
public class GeoDashboardAggregator
{
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IGeoPillarService _pillarService;
    private readonly IPromptCoverageService _coverageService;
    private readonly IActivityFeedService _activityService;
    private readonly IEngineScanService _engineScanService;
    private readonly IMediator _mediator;

    public GeoDashboardAggregator(
        IAiVisibilityRepository visibilityRepo,
        IGeoPillarService pillarService,
        IPromptCoverageService coverageService,
        IActivityFeedService activityService,
        IEngineScanService engineScanService,
        IMediator mediator)
    {
        _visibilityRepo = visibilityRepo;
        _pillarService = pillarService;
        _coverageService = coverageService;
        _activityService = activityService;
        _engineScanService = engineScanService;
        _mediator = mediator;
    }

    public async Task<GeoDashboardDto> BuildAsync(Guid organizationId, string range)
    {
        // First-ever visit for this org: no scan has run yet. Try to bootstrap real data
        // from whatever onboarding analysis already exists (persona/region/executive summary,
        // website profile, competitors) instead of showing fabricated numbers.
        var hasAnyScan = (await _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId)).Count > 0;
        if (!hasAnyScan)
        {
            await _mediator.Send(new RunScanCommand { OrganizationId = organizationId });
        }

        // ── Fan-out: fire all data fetches concurrently ─────────────
        var scansTask      = _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId);
        var sovTask         = _visibilityRepo.GetShareOfVoiceByOrgAsync(organizationId);
        var competitorsTask = _visibilityRepo.GetCompetitorsByOrgAsync(organizationId);
        var pillarsTask     = _pillarService.GetPillarsAsync(organizationId, range);
        var coverageTask    = _coverageService.GetCoverageAsync(organizationId, range);
        var activityTask    = _activityService.GetRecentEventsAsync(organizationId);
        var engineTask      = _engineScanService.GetScanStatsAsync(organizationId);

        await Task.WhenAll(scansTask, sovTask, competitorsTask, pillarsTask, coverageTask, activityTask, engineTask);

        var scans       = scansTask.Result;
        var sovDb       = sovTask.Result;
        var competitors = competitorsTask.Result;
        var pillars     = pillarsTask.Result;
        var coverage    = coverageTask.Result;
        var activity    = activityTask.Result;
        var (enginesScanned, promptsTracked) = engineTask.Result;

        // ── Scores ────────────────────────────────────────────────────
        var latestScan   = scans.LastOrDefault();
        var previousScan = scans.Count > 1 ? scans[scans.Count - 2] : null;
        var hasData      = latestScan != null;

        ScoreCardDto scores;

        if (latestScan == null)
        {
            // No scan exists (the org has no onboarding analysis to bootstrap from either) —
            // an honest zeroed-out state rather than fabricated numbers.
            var empty = new ScoreEntryDto(0, "+0%", "up");
            scores = new ScoreCardDto(empty, empty, empty, empty, empty, empty, empty, empty);
        }
        else
        {
            scores = new ScoreCardDto(
                new ScoreEntryDto(latestScan.VisibilityScore,   GetChangeStr(latestScan.VisibilityScore,   previousScan?.VisibilityScore),   GetDirection(latestScan.VisibilityScore,   previousScan?.VisibilityScore)),
                new ScoreEntryDto(latestScan.CitationScore,     GetChangeStr(latestScan.CitationScore,     previousScan?.CitationScore),     GetDirection(latestScan.CitationScore,     previousScan?.CitationScore)),
                new ScoreEntryDto(latestScan.SentimentScore,    GetChangeStr(latestScan.SentimentScore,    previousScan?.SentimentScore),    GetDirection(latestScan.SentimentScore,    previousScan?.SentimentScore)),
                new ScoreEntryDto(latestScan.CompetitorScore,   GetChangeStr(latestScan.CompetitorScore,   previousScan?.CompetitorScore),   GetDirection(latestScan.CompetitorScore,   previousScan?.CompetitorScore)),
                new ScoreEntryDto(latestScan.HallucinationRisk, GetChangeStr(latestScan.HallucinationRisk, previousScan?.HallucinationRisk), GetDirection(latestScan.HallucinationRisk, previousScan?.HallucinationRisk)),
                new ScoreEntryDto(latestScan.SeoHealth,         GetChangeStr(latestScan.SeoHealth,         previousScan?.SeoHealth),         GetDirection(latestScan.SeoHealth,         previousScan?.SeoHealth)),
                new ScoreEntryDto(latestScan.AeoReadiness,      GetChangeStr(latestScan.AeoReadiness,      previousScan?.AeoReadiness),      GetDirection(latestScan.AeoReadiness,      previousScan?.AeoReadiness)),
                new ScoreEntryDto(latestScan.GeoReadiness,      GetChangeStr(latestScan.GeoReadiness,      previousScan?.GeoReadiness),      GetDirection(latestScan.GeoReadiness,      previousScan?.GeoReadiness)));
        }

        // ── Trend ─────────────────────────────────────────────────────
        var trend = scans
            .Select((s, idx) => new TrendPointDto(idx + 1, s.VisibilityScore))
            .ToList();

        // ── Share of voice ────────────────────────────────────────────
        List<ShareOfVoiceEntryDto> shareOfVoice;
        var latestScanDate = sovDb.OrderByDescending(s => s.ScanDate).FirstOrDefault()?.ScanDate;

        shareOfVoice = latestScanDate != null
            ? sovDb.Where(s => s.ScanDate == latestScanDate)
                   .Select(s => new ShareOfVoiceEntryDto(s.CompetitorName, s.SharePercentage, s.ColorCode))
                   .ToList()
            : new List<ShareOfVoiceEntryDto>();

        // ── Header (composite from scorecard) ───────────────────────
        int compositeScore = (int)Math.Round(new[]
        {
            scores.VisibilityScore.Value,
            scores.CitationScore.Value,
            scores.SentimentScore.Value,
            scores.CompetitorScore.Value,
            scores.HallucinationRisk.Value,
            scores.SeoHealth.Value,
            scores.AeoReadiness.Value,
            scores.GeoReadiness.Value
        }.Average());

        // For composite change, use the GeoReadiness change as a proxy (it represents overall GEO)
        var compositeChange = scores.GeoReadiness.Change;

        // Industry average is approximated from tracked competitors' real authority scores
        // (there's no external cross-tenant benchmark data source). With no competitors tracked
        // yet, there's nothing honest to compare against, so it matches the composite (zero delta).
        var industryAverage = competitors.Count > 0
            ? (int)Math.Round(competitors.Average(c => c.Authority))
            : compositeScore;

        var header = new GeoDashboardHeaderDto(
            CompositeScore:  compositeScore,
            Grade:           GradeCalculator.ToGrade(compositeScore),
            IndustryAverage: industryAverage,
            DeltaVsIndustry: compositeScore - industryAverage,
            CompositeChange: compositeChange,
            EnginesScanned:  enginesScanned,
            PromptsTracked:  promptsTracked,
            Status:          hasData ? "live" : "pending");

        // ── Weakest pillar insight (derived) ────────────────────────
        WeakestPillarInsightDto? weakestInsight = null;
        if (pillars.Count > 0)
        {
            var weakest = pillars.MinBy(p => p.Score)!;
            weakestInsight = new WeakestPillarInsightDto(
                PillarKey: weakest.Key,
                Score:     weakest.Score,
                Message:   "Audit your key pages to find exactly what to fix.",
                CtaLabel:  "Run Page auditor",
                CtaLink:   "/geo-engine/page-auditor");
        }

        // ── Opportunity insight (derived) ───────────────────────────
        OpportunityInsightDto? opportunityInsight = null;
        if (coverage.Count > 0)
        {
            var lowestCoverage = coverage.MinBy(c => c.Percentage)!;
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
            Pillars:              pillars,
            WeakestPillarInsight: weakestInsight,
            PromptTypeCoverage:   coverage,
            OpportunityInsight:   opportunityInsight,
            WinsAndLosses:        activity,
            VerifyInsight:        verifyInsight);
    }

    // ── Private helpers (moved from controller) ─────────────────────
    private static string GetChangeStr(int current, int? prev)
    {
        if (prev is null or 0) return "+0%";
        var diff = current - prev.Value;
        var pct = Math.Round((decimal)diff / prev.Value * 100, 1);
        return pct >= 0 ? $"+{pct}%" : $"{pct}%";
    }

    private static string GetDirection(int current, int? prev)
    {
        if (prev == null) return "up";
        return current >= prev.Value ? "up" : "down";
    }
}
