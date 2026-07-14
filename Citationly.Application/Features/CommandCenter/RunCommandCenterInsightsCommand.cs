using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.CommandCenter;

public class RunCommandCenterInsightsCommand : IRequest<RunCommandCenterInsightsResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunCommandCenterInsightsResult(bool Success, string Message);

public class RunCommandCenterInsightsCommandHandler : IRequestHandler<RunCommandCenterInsightsCommand, RunCommandCenterInsightsResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepo;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepo;
    private readonly ICitationScanSnapshotRepository _citationSnapshotRepo;
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepo;
    private readonly ICommandCenterInsightRepository _snapshotRepo;
    private readonly IOpenAiService _openAiService;

    public RunCommandCenterInsightsCommandHandler(
        IWebsiteRepository websiteRepository,
        IAiVisibilityRepository visibilityRepo,
        ICompetitorSnapshotRepository competitorSnapshotRepo,
        IVisibilitySnapshotRepository visibilitySnapshotRepo,
        ICitationScanSnapshotRepository citationSnapshotRepo,
        IBrandPulseSnapshotRepository brandPulseSnapshotRepo,
        ICommandCenterInsightRepository snapshotRepo,
        IOpenAiService openAiService)
    {
        _websiteRepository = websiteRepository;
        _visibilityRepo = visibilityRepo;
        _competitorSnapshotRepo = competitorSnapshotRepo;
        _visibilitySnapshotRepo = visibilitySnapshotRepo;
        _citationSnapshotRepo = citationSnapshotRepo;
        _brandPulseSnapshotRepo = brandPulseSnapshotRepo;
        _snapshotRepo = snapshotRepo;
        _openAiService = openAiService;
    }

    public async Task<RunCommandCenterInsightsResult> Handle(RunCommandCenterInsightsCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        if (profile == null)
        {
            return new RunCommandCenterInsightsResult(false, "No analyzed data found yet for this organization.");
        }

        var scans = (await _visibilityRepo.GetHistoricalScansByOrgAsync(orgId)).OrderBy(s => s.ScanDate).ToList();
        var latestScan = scans.LastOrDefault();
        var previousScan = scans.Count > 1 ? scans[^2] : null;

        var competitorScanDate = await _competitorSnapshotRepo.GetLatestScanDateAsync(orgId);
        var competitorSnapshots = competitorScanDate.HasValue
            ? await _competitorSnapshotRepo.GetSnapshotsByScanDateAsync(orgId, competitorScanDate.Value)
            : new();
        var you = competitorSnapshots.FirstOrDefault(s => s.IsYou);

        var visLatestDate = await _visibilitySnapshotRepo.GetLatestScanDateAsync(orgId);
        var visSummary = visLatestDate.HasValue ? await _visibilitySnapshotRepo.GetSummaryByScanDateAsync(orgId, visLatestDate.Value) : null;

        var citLatestDate = await _citationSnapshotRepo.GetLatestScanDateAsync(orgId);
        var citSummary = citLatestDate.HasValue ? await _citationSnapshotRepo.GetSummaryByScanDateAsync(orgId, citLatestDate.Value) : null;

        var bpLatestDate = await _brandPulseSnapshotRepo.GetLatestScanDateAsync(orgId);
        var bpSummary = bpLatestDate.HasValue ? await _brandPulseSnapshotRepo.GetSummaryByScanDateAsync(orgId, bpLatestDate.Value) : null;

        const string systemPrompt =
            "You are writing a short executive summary for a business dashboard. " +
            "Based ONLY on the real metrics provided, return ONLY a JSON object with EXACTLY this key: " +
            "\"insights\": an array of 4-6 short sentences (max ~20 words each), each referencing a SPECIFIC real number given below, " +
            "wrapping the key number/phrase in <b></b> tags (e.g. \"AI visibility is now <b>84/100</b>, up from 79 last scan.\"). " +
            "Do not invent numbers that were not provided. If a metric isn't provided, don't write about it.";

        var sb = new StringBuilder();
        sb.AppendLine($"Business: {profile.BusinessName}");
        if (latestScan != null)
        {
            sb.AppendLine($"Visibility score: {latestScan.VisibilityScore} (previous: {previousScan?.VisibilityScore.ToString() ?? "none"})");
            sb.AppendLine($"Citation score: {latestScan.CitationScore} (previous: {previousScan?.CitationScore.ToString() ?? "none"})");
            sb.AppendLine($"GEO readiness: {latestScan.GeoReadiness}, SEO health: {latestScan.SeoHealth}, AEO readiness: {latestScan.AeoReadiness}");
        }
        if (you != null) sb.AppendLine($"Your share of voice vs competitors: {you.ShareOfVoice}% (rank {you.Rank})");
        if (visSummary != null) sb.AppendLine($"AI platform visibility composite: {visSummary.CompositeScore}/100");
        if (citSummary != null) sb.AppendLine($"Citation quality score: {citSummary.CompositeQualityScore}/100, models referencing you: {citSummary.ModelsReferencingCount}/{citSummary.ModelsTrackedCount}");
        if (bpSummary != null) sb.AppendLine($"Brand health: {bpSummary.BrandHealth}/100, brand trust: {bpSummary.BrandTrust}/100, messaging consistency: {bpSummary.MessagingConsistency}%");

        var insights = new List<string>();
        try
        {
            var raw = await _openAiService.GenerateContentAsync(sb.ToString(), systemPrompt, requireJson: true);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("insights", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) insights.Add(text);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Defensive fallback: a couple of honest, deterministic sentences from whatever real
            // data we do have, rather than surfacing an error or fabricating content.
            if (latestScan != null)
                insights.Add($"AI visibility is at <b>{latestScan.VisibilityScore}/100</b>.");
            if (you != null)
                insights.Add($"Your current share of voice is <b>{you.ShareOfVoice}%</b>.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);
        await _snapshotRepo.InsertAsync(new()
        {
            OrganizationId = orgId,
            ScanDate = today,
            InsightsJson = JsonSerializer.Serialize(insights)
        });

        return new RunCommandCenterInsightsResult(true, "Command center insights generated.");
    }
}
