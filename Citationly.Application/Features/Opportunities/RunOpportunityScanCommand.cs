using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Opportunities;

public class RunOpportunityScanCommand : IRequest<RunOpportunityScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunOpportunityScanResult(bool Success, string Message);

public class RunOpportunityScanCommandHandler : IRequestHandler<RunOpportunityScanCommand, RunOpportunityScanResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepo;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepo;
    private readonly ICitationScanSnapshotRepository _citationSnapshotRepo;
    private readonly IOpportunitySnapshotRepository _snapshotRepo;
    private readonly IOpenAiService _openAiService;

    public RunOpportunityScanCommandHandler(
        IWebsiteRepository websiteRepository,
        IAiVisibilityRepository visibilityRepo,
        ICompetitorSnapshotRepository competitorSnapshotRepo,
        IVisibilitySnapshotRepository visibilitySnapshotRepo,
        ICitationScanSnapshotRepository citationSnapshotRepo,
        IOpportunitySnapshotRepository snapshotRepo,
        IOpenAiService openAiService)
    {
        _websiteRepository = websiteRepository;
        _visibilityRepo = visibilityRepo;
        _competitorSnapshotRepo = competitorSnapshotRepo;
        _visibilitySnapshotRepo = visibilitySnapshotRepo;
        _citationSnapshotRepo = citationSnapshotRepo;
        _snapshotRepo = snapshotRepo;
        _openAiService = openAiService;
    }

    public async Task<RunOpportunityScanResult> Handle(RunOpportunityScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        if (profile == null)
        {
            return new RunOpportunityScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run an opportunity scan.");
        }

        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(orgId);
        var scans = (await _visibilityRepo.GetHistoricalScansByOrgAsync(orgId)).OrderBy(s => s.ScanDate).ToList();
        var latestScan = scans.LastOrDefault();

        var competitorScanDate = await _competitorSnapshotRepo.GetLatestScanDateAsync(orgId);
        var competitorSnapshots = competitorScanDate.HasValue
            ? await _competitorSnapshotRepo.GetSnapshotsByScanDateAsync(orgId, competitorScanDate.Value)
            : new List<CompetitorSnapshot>();
        var threats = competitorSnapshots.Where(c => !c.IsYou && (c.Threat == "high" || c.Threat == "med")).ToList();

        var visLatestDate = await _visibilitySnapshotRepo.GetLatestScanDateAsync(orgId);
        var weakPlatforms = visLatestDate.HasValue
            ? (await _visibilitySnapshotRepo.GetPlatformSnapshotsByScanDateAsync(orgId, visLatestDate.Value)).Where(p => p.Status is "Weak" or "Developing").ToList()
            : new();

        var citLatestDate = await _citationSnapshotRepo.GetLatestScanDateAsync(orgId);
        var citOpportunities = citLatestDate.HasValue
            ? (await _citationSnapshotRepo.GetSourceSnapshotsByScanDateAsync(orgId, citLatestDate.Value)).OrderByDescending(s => s.OpportunityScore).Take(6).ToList()
            : new();

        var (systemPrompt, userPrompt) = BuildPrompt(profile, executiveSummary, latestScan, threats, weakPlatforms, citOpportunities);
        var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
        var opportunities = ParseOpportunities(raw);

        if (opportunities.Count == 0)
        {
            return new RunOpportunityScanResult(false, "No opportunities could be identified for this organization.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        for (int i = 0; i < opportunities.Count; i++)
        {
            var o = opportunities[i];
            await _snapshotRepo.InsertAsync(new OpportunitySnapshot
            {
                OrganizationId = orgId,
                ScanDate = today,
                OpportunityKey = $"OM-{i + 1:D3}",
                Category = o.Category,
                Title = o.Title,
                Summary = o.Summary,
                WhyItMatters = o.WhyItMatters,
                Score = o.Score,
                Effort = o.Effort,
                EstimatedGainPct = o.EstimatedGainPct,
                Eta = o.Eta,
                CompetitorContext = o.CompetitorContext,
                ChecklistJson = JsonSerializer.Serialize(o.Checklist)
            });
        }

        return new RunOpportunityScanResult(true, "Opportunity scan complete.");
    }

    private static (string SystemPrompt, string UserPrompt) BuildPrompt(
        WebsiteProfile profile,
        ExecutiveSummaryData? executiveSummary,
        HistoricalScan? latestScan,
        List<CompetitorSnapshot> threats,
        List<VisibilityPlatformSnapshot> weakPlatforms,
        List<CitationSourceSnapshot> citOpportunities)
    {
        const string systemPrompt =
            "You are a GEO/AEO growth strategist identifying concrete optimization opportunities for a business's AI search presence. " +
            "Based ONLY on the real signals provided, return ONLY a JSON object with EXACTLY this key: " +
            "\"opportunities\": an array of 8-12 objects, each: " +
            "{\"category\":string (e.g. 'GEO Accuracy','AI Visibility','Authority','Entity Coverage','Content','GEO Optimization'), " +
            "\"title\":string (short, action-oriented), " +
            "\"summary\":string (1 sentence describing the specific real gap), " +
            "\"whyItMatters\":string (1 sentence rationale grounded in the real signals given), " +
            "\"score\":int 0-100 (AI-judged opportunity/impact score), " +
            "\"effort\":int 0-100 (implementation difficulty — higher is harder), " +
            "\"estimatedGainPct\":number 0-30 (plausible AI visibility gain percentage if implemented), " +
            "\"eta\":string (realistic time estimate, e.g. '2 hours', '5 days'), " +
            "\"competitorContext\":string (1 short sentence — reference a real competitor/platform signal if one was given, otherwise a generic but honest statement), " +
            "\"checklist\":array of 3-5 short concrete action item strings}. " +
            "Ground every opportunity in the real signals given — do not invent unrelated facts or fabricate specific competitor names not provided.";

        var sb = new StringBuilder();
        sb.AppendLine($"Business: {profile.BusinessName} ({profile.WebsiteUrl})");
        var rawProfile = profile.RawProfileJson;
        if (rawProfile.Length > 1500) rawProfile = rawProfile[..1500];
        sb.AppendLine($"Website profile: {rawProfile}");

        if (executiveSummary != null)
        {
            sb.AppendLine($"Executive summary: {executiveSummary.BusinessOverview}");
            sb.AppendLine($"Competitor position: {executiveSummary.CompetitorPosition}");
        }

        if (latestScan != null)
        {
            sb.AppendLine($"Latest real scan: visibility {latestScan.VisibilityScore}, citation {latestScan.CitationScore}, GEO readiness {latestScan.GeoReadiness}, SEO health {latestScan.SeoHealth}, AEO readiness {latestScan.AeoReadiness}.");
        }

        if (threats.Count > 0)
        {
            sb.AppendLine("Real competitor threats: " + string.Join(", ", threats.Select(t => $"{t.Name} (rank #{t.Rank}, {t.Threat} threat, {t.ShareOfVoice}% share of voice)")));
        }

        if (weakPlatforms.Count > 0)
        {
            sb.AppendLine("Real underperforming AI platforms: " + string.Join(", ", weakPlatforms.Select(p => $"{p.Platform} (score {p.Score}/100, {p.Status})")));
        }

        if (citOpportunities.Count > 0)
        {
            sb.AppendLine("Real high-opportunity citation sources: " + string.Join(", ", citOpportunities.Select(c => $"{c.Source} (opportunity {c.OpportunityScore}/100 — {c.Reason})")));
        }

        return (systemPrompt, sb.ToString());
    }

    private record ParsedOpportunity(string Category, string Title, string Summary, string WhyItMatters, int Score, int Effort, double EstimatedGainPct, string Eta, string CompetitorContext, List<string> Checklist);

    private static List<ParsedOpportunity> ParseOpportunities(string raw)
    {
        var results = new List<ParsedOpportunity>();
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("opportunities", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return results;

            foreach (var item in arr.EnumerateArray())
            {
                string GetStr(string key, string fallback = "") =>
                    item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
                int GetInt(string key, int fallback) =>
                    item.TryGetProperty(key, out var v) && v.TryGetInt32(out var iv) ? Math.Clamp(iv, 0, 100) : fallback;
                double GetDouble(string key, double fallback) =>
                    item.TryGetProperty(key, out var v) && v.TryGetDouble(out var dv) ? Math.Clamp(dv, 0, 30) : fallback;

                var title = GetStr("title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                var checklist = new List<string>();
                if (item.TryGetProperty("checklist", out var cl) && cl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cl.EnumerateArray())
                    {
                        if (c.ValueKind == JsonValueKind.String)
                        {
                            var text = c.GetString();
                            if (!string.IsNullOrWhiteSpace(text)) checklist.Add(text);
                        }
                    }
                }

                results.Add(new ParsedOpportunity(
                    Category: GetStr("category", "GEO Optimization"),
                    Title: title,
                    Summary: GetStr("summary"),
                    WhyItMatters: GetStr("whyItMatters"),
                    Score: GetInt("score", 50),
                    Effort: GetInt("effort", 50),
                    EstimatedGainPct: GetDouble("estimatedGainPct", 5),
                    Eta: GetStr("eta", "A few days"),
                    CompetitorContext: GetStr("competitorContext"),
                    Checklist: checklist));
            }
        }
        catch (Exception)
        {
            return new List<ParsedOpportunity>();
        }

        return results;
    }
}
