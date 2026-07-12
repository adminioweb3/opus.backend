using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Opportunities;

public class OpportunityFinderAggregator
{
    private const int ScanIntervalDays = 7;

    private readonly IOpportunitySnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;

    public OpportunityFinderAggregator(IOpportunitySnapshotRepository snapshotRepo, IMediator mediator)
    {
        _snapshotRepo = snapshotRepo;
        _mediator = mediator;
    }

    public async Task<object> BuildAsync(Guid organizationId, string range)
    {
        var lookbackDays = range switch { "7D" => 7, "90D" => 90, _ => 30 };

        await _snapshotRepo.EnsureTableCreatedAsync();

        var latestScanDate = await _snapshotRepo.GetLatestScanDateAsync(organizationId);
        if (latestScanDate == null)
        {
            await _mediator.Send(new RunOpportunityScanCommand { OrganizationId = organizationId });
            latestScanDate = await _snapshotRepo.GetLatestScanDateAsync(organizationId);
        }

        if (latestScanDate == null)
        {
            return new { hasData = false };
        }

        var snapshots = await _snapshotRepo.GetSnapshotsByScanDateAsync(organizationId, latestScanDate.Value);
        var opportunities = snapshots.Select(ToViewModel).ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceLastScan = today.DayNumber - latestScanDate.Value.DayNumber;
        var canRunDeepScan = daysSinceLastScan >= ScanIntervalDays;
        var nextEligibleDate = latestScanDate.Value.AddDays(ScanIntervalDays);

        var cutoff = today.AddDays(-lookbackDays);
        var history = await _snapshotRepo.GetRecentHistoryAsync(organizationId, 60);
        var historyInRange = history.Where(h => h.ScanDate >= cutoff).ToList();
        var trend = historyInRange
            .GroupBy(h => h.ScanDate)
            .OrderBy(g => g.Key)
            .Select(g => new { date = g.Key.ToString("MMM dd"), avgScore = (int)Math.Round(g.Average(x => x.Score)), count = g.Count() })
            .ToList();

        var avgGainPct = snapshots.Count > 0 ? Math.Round(snapshots.Average(s => s.EstimatedGainPct), 1) : 0;
        var criticalCount = snapshots.Count(s => Priority(s.Score) == "Critical");
        var quickWinsCount = snapshots.Count(s => Quadrant(s.Score, s.Effort) == "high-low");

        return new
        {
            hasData = true,
            lastScanDate = latestScanDate.Value.ToString("yyyy-MM-dd"),
            canRunDeepScan,
            nextEligibleDate = nextEligibleDate.ToString("yyyy-MM-dd"),
            daysUntilEligible = canRunDeepScan ? 0 : ScanIntervalDays - daysSinceLastScan,
            opportunities,
            kpis = new
            {
                totalOpportunities = snapshots.Count,
                estimatedImpactScore = snapshots.Count > 0 ? (int)Math.Round(snapshots.Average(s => s.Score)) : 0,
                criticalCount,
                quickWinsCount
            },
            forecast = new
            {
                potentialGainPct = avgGainPct,
                trend
            }
        };
    }

    private static string DifficultyLabel(int effort) => effort < 35 ? "Low" : effort < 70 ? "Medium" : "High";

    private static string Quadrant(int score, int effort) =>
        score >= 70 && effort < 50 ? "high-low" :
        score >= 70 ? "high-high" :
        effort < 50 ? "low-low" : "low-high";

    private static string Priority(int score) =>
        score >= 90 ? "Critical" : score >= 75 ? "High" : score >= 55 ? "Medium" : "Low";

    private static string Badge(string quadrant, string priority, string category)
    {
        if (quadrant == "high-low") return "⚡ Quick Win";
        if (priority == "Critical") return "🚨 Critical";
        if (category.Contains("Authority", StringComparison.OrdinalIgnoreCase) || category.Contains("Competitor", StringComparison.OrdinalIgnoreCase)) return "🏆 Competitive Gap";
        if (category.Contains("GEO", StringComparison.OrdinalIgnoreCase) || category.Contains("Entity", StringComparison.OrdinalIgnoreCase)) return "🤖 AI Boost";
        return "📈 Momentum";
    }

    private static object ToViewModel(OpportunitySnapshot s)
    {
        var difficulty = DifficultyLabel(s.Effort);
        var quadrant = Quadrant(s.Score, s.Effort);
        var priority = Priority(s.Score);
        var badge = Badge(quadrant, priority, s.Category);

        List<string> checklist;
        try { checklist = JsonSerializer.Deserialize<List<string>>(s.ChecklistJson) ?? new(); }
        catch { checklist = new(); }

        return new
        {
            id = s.OpportunityKey,
            category = s.Category,
            title = s.Title,
            summary = s.Summary,
            whyItMatters = s.WhyItMatters,
            score = s.Score,
            effort = s.Effort,
            difficulty,
            estimatedGainPct = s.EstimatedGainPct,
            eta = s.Eta,
            competitorContext = s.CompetitorContext,
            checklist,
            quadrant,
            priority,
            badge
        };
    }
}
