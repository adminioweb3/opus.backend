using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.CommandCenter;

/// <summary>
/// Assembles the Command Center response entirely from data already computed by the five
/// per-feature recurring scans (GEO/HistoricalScans, Competitor, Visibility, Citation, Brand
/// Pulse) — no new scoring logic, just real cross-feature aggregation. The only genuinely new
/// AI content is the weekly business-insights narrative (<see cref="RunCommandCenterInsightsCommand"/>).
/// </summary>
public class CommandCenterAggregator
{
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepo;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepo;
    private readonly ICitationScanSnapshotRepository _citationSnapshotRepo;
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepo;
    private readonly ICommandCenterInsightRepository _insightRepo;
    private readonly IMediator _mediator;

    public CommandCenterAggregator(
        IAiVisibilityRepository visibilityRepo,
        ICompetitorSnapshotRepository competitorSnapshotRepo,
        IVisibilitySnapshotRepository visibilitySnapshotRepo,
        ICitationScanSnapshotRepository citationSnapshotRepo,
        IBrandPulseSnapshotRepository brandPulseSnapshotRepo,
        ICommandCenterInsightRepository insightRepo,
        IMediator mediator)
    {
        _visibilityRepo = visibilityRepo;
        _competitorSnapshotRepo = competitorSnapshotRepo;
        _visibilitySnapshotRepo = visibilitySnapshotRepo;
        _citationSnapshotRepo = citationSnapshotRepo;
        _brandPulseSnapshotRepo = brandPulseSnapshotRepo;
        _insightRepo = insightRepo;
        _mediator = mediator;
    }

    public async Task<object> BuildAsync(Guid organizationId, string range)
    {
        var lookbackDays = range switch { "7D" => 7, "90D" => 90, _ => 30 };
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-lookbackDays);

        await _insightRepo.EnsureTableCreatedAsync();
        if (await _insightRepo.GetLatestScanDateAsync(organizationId) == null)
        {
            await _mediator.Send(new RunCommandCenterInsightsCommand { OrganizationId = organizationId });
        }

        var scans = (await _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId)).OrderBy(s => s.ScanDate).ToList();
        var latestScan = scans.LastOrDefault();
        var previousScan = scans.Count > 1 ? scans[^2] : null;

        if (latestScan == null)
        {
            return new { hasData = false };
        }

        var competitorScanDate = await _competitorSnapshotRepo.GetLatestScanDateAsync(organizationId);
        var competitorSnapshots = competitorScanDate.HasValue
            ? await _competitorSnapshotRepo.GetSnapshotsByScanDateAsync(organizationId, competitorScanDate.Value)
            : new List<CompetitorSnapshot>();
        var you = competitorSnapshots.FirstOrDefault(s => s.IsYou);
        var competitorHistory = await _competitorSnapshotRepo.GetRecentHistoryAsync(organizationId, 20);
        var youHistoryInRange = competitorHistory.Where(s => s.IsYou && s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        var previousYouShare = youHistoryInRange.Count > 1 ? youHistoryInRange.First().ShareOfVoice : (int?)null;

        var visLatestDate = await _visibilitySnapshotRepo.GetLatestScanDateAsync(organizationId);
        var visPlatforms = visLatestDate.HasValue
            ? await _visibilitySnapshotRepo.GetPlatformSnapshotsByScanDateAsync(organizationId, visLatestDate.Value)
            : new List<VisibilityPlatformSnapshot>();

        var citLatestDate = await _citationSnapshotRepo.GetLatestScanDateAsync(organizationId);
        var citSummary = citLatestDate.HasValue ? await _citationSnapshotRepo.GetSummaryByScanDateAsync(organizationId, citLatestDate.Value) : null;
        var citSources = citLatestDate.HasValue ? await _citationSnapshotRepo.GetSourceSnapshotsByScanDateAsync(organizationId, citLatestDate.Value) : new List<CitationSourceSnapshot>();
        var citHistory = await _citationSnapshotRepo.GetRecentSummaryHistoryAsync(organizationId, 20);
        var citInRange = citHistory.Where(s => s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        var previousAuthority = citInRange.Count > 1 ? citInRange.First().AverageAuthorityScore : (int?)null;

        var bpLatestDate = await _brandPulseSnapshotRepo.GetLatestScanDateAsync(organizationId);
        var bpSummary = bpLatestDate.HasValue ? await _brandPulseSnapshotRepo.GetSummaryByScanDateAsync(organizationId, bpLatestDate.Value) : null;
        var bpHistory = await _brandPulseSnapshotRepo.GetRecentSummaryHistoryAsync(organizationId, 20);
        var bpInRange = bpHistory.Where(s => s.ScanDate >= cutoff).OrderBy(s => s.ScanDate).ToList();
        var previousMessaging = bpInRange.Count > 1 ? bpInRange.First().MessagingConsistency : (int?)null;

        var insightSnapshot = await _insightRepo.GetLatestAsync(organizationId);

        // ── Scan coverage KPI: how many of the 5 recurring scans have run at all ──
        var scanCoverage = new[]
        {
            scans.Count > 0, competitorScanDate.HasValue, visLatestDate.HasValue, citLatestDate.HasValue, bpLatestDate.HasValue
        }.Count(x => x);

        // ── Action items: real, aggregated across the 4 sibling features ──
        var actionItems = new List<object>();
        foreach (var s in citSources.OrderByDescending(s => s.OpportunityScore).Take(2))
        {
            actionItems.Add(new { source = "Citation Intelligence", title = $"Opportunity: {s.Source}", detail = s.Reason, severity = s.OpportunityScore >= 70 ? "High" : "Medium", link = "/dashboard/citation-intelligence" });
        }
        foreach (var c in competitorSnapshots.Where(c => !c.IsYou && (c.Threat == "high" || c.Threat == "med")).OrderByDescending(c => c.Threat == "high").Take(2))
        {
            actionItems.Add(new { source = "Competitor Watch", title = $"{c.Name} is a {(c.Threat == "high" ? "high" : "moderate")} threat", detail = $"Share of voice {c.ShareOfVoice}%, rank #{c.Rank}.", severity = c.Threat == "high" ? "High" : "Medium", link = "/dashboard/competitor-watch" });
        }
        if (bpSummary != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(bpSummary.AccuracyFlagsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var flag in doc.RootElement.EnumerateArray().Take(2))
                    {
                        var claim = flag.TryGetProperty("claim", out var cv) ? cv.GetString() ?? "" : "";
                        var detail = flag.TryGetProperty("detail", out var dv) ? dv.GetString() ?? "" : "";
                        var severity = flag.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "Medium" : "Medium";
                        if (!string.IsNullOrWhiteSpace(claim))
                            actionItems.Add(new { source = "Brand Pulse", title = claim, detail, severity, link = "/dashboard/brand-pulse" });
                    }
                }
            }
            catch { /* malformed snapshot JSON — skip, not fatal to the page */ }
        }
        foreach (var p in visPlatforms.Where(p => p.Status == "Weak").Take(2))
        {
            actionItems.Add(new { source = "Visibility Radar", title = $"{p.Platform} visibility is weak", detail = $"Score {p.Score}/100 — {p.Citations} citation signal.", severity = "Medium", link = "/dashboard/visibility-radar" });
        }

        // ── Alert center: real deltas + Brand Pulse's own already-judged alerts ──
        var alerts = new List<object>();
        void AddRegressionAlert(string label, int current, int? previous)
        {
            if (previous.HasValue && current < previous.Value)
            {
                alerts.Add(new { title = $"{label} dropped", message = $"{label} fell from {previous.Value} to {current}.", severity = "High" });
            }
        }
        AddRegressionAlert("Visibility score", latestScan.VisibilityScore, previousScan?.VisibilityScore);
        AddRegressionAlert("Citation score", latestScan.CitationScore, previousScan?.CitationScore);
        AddRegressionAlert("SEO health", latestScan.SeoHealth, previousScan?.SeoHealth);
        AddRegressionAlert("GEO readiness", latestScan.GeoReadiness, previousScan?.GeoReadiness);
        foreach (var c in competitorSnapshots.Where(c => !c.IsYou && c.Threat == "high").Take(2))
        {
            alerts.Add(new { title = $"{c.Name} is a high threat", message = $"Rank #{c.Rank}, share of voice {c.ShareOfVoice}%.", severity = "High" });
        }
        foreach (var p in visPlatforms.Where(p => p.Status == "Weak").Take(2))
        {
            alerts.Add(new { title = $"{p.Platform} visibility is weak", message = $"Score {p.Score}/100.", severity = "Medium" });
        }
        if (bpSummary != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(bpSummary.AlertsJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in doc.RootElement.EnumerateArray().Take(3))
                    {
                        var title = a.TryGetProperty("title", out var tv) ? tv.GetString() ?? "" : "";
                        var message = a.TryGetProperty("message", out var mv) ? mv.GetString() ?? "" : "";
                        var type = a.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "warning" : "warning";
                        if (!string.IsNullOrWhiteSpace(title))
                            alerts.Add(new { title, message, severity = type == "risk" ? "High" : type == "win" ? "Good" : "Medium" });
                    }
                }
            }
            catch { /* malformed snapshot JSON — skip */ }
        }
        alerts = alerts.Take(8).ToList();

        // ── Performance breakdown: real fields + real proxies from sibling scans ──
        object BreakdownCard(string name, int cur, int? prev) => new
        {
            name,
            cur,
            prev = prev ?? cur,
            insight = prev.HasValue
                ? (cur >= prev.Value ? $"Up {cur - prev.Value} pts vs last scan." : $"Down {prev.Value - cur} pts vs last scan.")
                : "First scan — no prior comparison yet."
        };

        var breakdown = new List<object>
        {
            BreakdownCard("AI visibility", latestScan.VisibilityScore, previousScan?.VisibilityScore),
            BreakdownCard("Citation", latestScan.CitationScore, previousScan?.CitationScore),
            BreakdownCard("GEO", latestScan.GeoReadiness, previousScan?.GeoReadiness),
            BreakdownCard("SEO", latestScan.SeoHealth, previousScan?.SeoHealth),
            BreakdownCard("AEO", latestScan.AeoReadiness, previousScan?.AeoReadiness),
        };
        if (bpSummary != null) breakdown.Add(BreakdownCard("Messaging consistency", bpSummary.MessagingConsistency, previousMessaging));
        if (citSummary != null) breakdown.Add(BreakdownCard("Citation authority", citSummary.AverageAuthorityScore, previousAuthority));
        if (citSummary != null && citSummary.ModelsTrackedCount > 0)
        {
            var entityCoverage = (int)Math.Round(citSummary.ModelsReferencingCount / (double)citSummary.ModelsTrackedCount * 100);
            breakdown.Add(new { name = "Entity coverage", cur = entityCoverage, prev = entityCoverage, insight = $"Recognized by {citSummary.ModelsReferencingCount} of {citSummary.ModelsTrackedCount} tracked AI platforms." });
        }

        // ── KPIs ──
        var overallScore = (int)Math.Round(new[] { latestScan.VisibilityScore, latestScan.CitationScore, latestScan.SentimentScore, latestScan.CompetitorScore }.Average());
        var previousOverall = previousScan != null
            ? (int?)Math.Round(new[] { previousScan.VisibilityScore, previousScan.CitationScore, previousScan.SentimentScore, previousScan.CompetitorScore }.Average())
            : null;

        object Kpi(string label, int val, string suffix, int? delta, List<int>? spark = null) => new { label, val, suffix, delta = delta ?? 0, spark = spark ?? new List<int>() };

        var recentScans = scans.TakeLast(8).ToList();
        var overallHistory = recentScans.Select(s => (int)Math.Round(new[] { s.VisibilityScore, s.CitationScore, s.SentimentScore, s.CompetitorScore }.Average())).ToList();
        var visibilityHistory = recentScans.Select(s => s.VisibilityScore).ToList();
        var citationHistory = recentScans.Select(s => s.CitationScore).ToList();

        var kpis = new object[]
        {
            Kpi("Overall performance score", overallScore, "pts", previousOverall.HasValue ? overallScore - previousOverall.Value : null, overallHistory),
            Kpi("AI visibility growth", latestScan.VisibilityScore, "pts", previousScan != null ? latestScan.VisibilityScore - previousScan.VisibilityScore : null, visibilityHistory),
            Kpi("Citation growth", latestScan.CitationScore, "pts", previousScan != null ? latestScan.CitationScore - previousScan.CitationScore : null, citationHistory),
            Kpi("Competitive share of voice", you?.ShareOfVoice ?? 0, "%", previousYouShare.HasValue ? (you?.ShareOfVoice ?? 0) - previousYouShare.Value : null),
            Kpi("Open action items", actionItems.Count, "", null),
            Kpi("Scan coverage", scanCoverage, "/5", null),
        };

        List<string> insights = new();
        if (insightSnapshot != null)
        {
            try
            {
                insights = JsonSerializer.Deserialize<List<string>>(insightSnapshot.InsightsJson) ?? new();
            }
            catch { /* leave empty — frontend renders an honest empty state */ }
        }

        return new
        {
            hasData = true,
            lastScanDate = latestScan.ScanDate.ToString("yyyy-MM-dd"),
            kpis,
            breakdown,
            actionItems,
            alerts,
            insights
        };
    }
}
