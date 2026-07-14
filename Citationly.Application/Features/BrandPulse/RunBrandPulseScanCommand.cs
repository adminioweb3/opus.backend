using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.BrandPulse;

public class RunBrandPulseScanCommand : IRequest<RunBrandPulseScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunBrandPulseScanResult(bool Success, string Message);

public class RunBrandPulseScanCommandHandler : IRequestHandler<RunBrandPulseScanCommand, RunBrandPulseScanResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepo;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepo;
    private readonly IBrandPulseSnapshotRepository _snapshotRepo;
    private readonly IOpenAiService _openAiService;

    public RunBrandPulseScanCommandHandler(
        IWebsiteRepository websiteRepository,
        IAiVisibilityRepository visibilityRepo,
        ICompetitorSnapshotRepository competitorSnapshotRepo,
        IVisibilitySnapshotRepository visibilitySnapshotRepo,
        IBrandPulseSnapshotRepository snapshotRepo,
        IOpenAiService openAiService)
    {
        _websiteRepository = websiteRepository;
        _visibilityRepo = visibilityRepo;
        _competitorSnapshotRepo = competitorSnapshotRepo;
        _visibilitySnapshotRepo = visibilitySnapshotRepo;
        _snapshotRepo = snapshotRepo;
        _openAiService = openAiService;
    }

    public async Task<RunBrandPulseScanResult> Handle(RunBrandPulseScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        if (profile == null)
        {
            return new RunBrandPulseScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run a brand pulse scan.");
        }

        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(orgId);
        var scans = (await _visibilityRepo.GetHistoricalScansByOrgAsync(orgId)).OrderBy(s => s.ScanDate).ToList();
        var latestScan = scans.LastOrDefault();
        var prompts = (await _websiteRepository.GetAiSearchPromptsAsync(orgId)).ToList();

        // Real, already-computed weekly data from the sibling Competitor/Visibility scans —
        // reused here rather than re-derived, so share-of-perception and per-model
        // confidence stay consistent with what those pages already show.
        var competitorScanDate = await _competitorSnapshotRepo.GetLatestScanDateAsync(orgId);
        var competitorSnapshots = competitorScanDate.HasValue
            ? await _competitorSnapshotRepo.GetSnapshotsByScanDateAsync(orgId, competitorScanDate.Value)
            : new List<CompetitorSnapshot>();

        var visibilityScanDate = await _visibilitySnapshotRepo.GetLatestScanDateAsync(orgId);
        var platformSnapshots = visibilityScanDate.HasValue
            ? await _visibilitySnapshotRepo.GetPlatformSnapshotsByScanDateAsync(orgId, visibilityScanDate.Value)
            : new List<VisibilityPlatformSnapshot>();

        var (systemPrompt, userPrompt) = BuildPrompt(profile, executiveSummary, latestScan, prompts, competitorSnapshots, platformSnapshots);
        var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
        var judged = ParseJudgment(raw, latestScan);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        // Merge AI-judged sentiment/themes with the real, already-computed platform score —
        // the confidence number itself is real data, not an AI guess.
        var modelInsights = platformSnapshots.Select(p =>
        {
            var match = judged.ModelInsights.FirstOrDefault(m => string.Equals(m.Platform, p.Platform, StringComparison.OrdinalIgnoreCase));
            return new
            {
                platform = p.Platform,
                confidence = p.Score,
                sentiment = match?.Sentiment ?? "neu",
                themes = match?.Themes ?? new List<string>(),
                flag = match?.Flag ?? false
            };
        }).ToList();

        await _snapshotRepo.InsertSummaryAsync(new BrandPulseScanSummary
        {
            OrganizationId = orgId,
            ScanDate = today,
            BrandHealth = judged.BrandHealth,
            AiConfidence = judged.AiConfidence,
            MessagingConsistency = judged.MessagingConsistency,
            BrandTrust = judged.BrandTrust,
            SentimentPositive = judged.SentimentPositive,
            SentimentNeutral = judged.SentimentNeutral,
            SentimentNegative = judged.SentimentNegative,
            AlertsJson = JsonSerializer.Serialize(judged.Alerts),
            ModelInsightsJson = JsonSerializer.Serialize(modelInsights),
            AccuracyFlagsJson = JsonSerializer.Serialize(judged.AccuracyFlags),
            PromptEvidenceJson = JsonSerializer.Serialize(judged.PromptEvidence)
        });

        return new RunBrandPulseScanResult(true, "Brand pulse scan complete.");
    }

    private static (string SystemPrompt, string UserPrompt) BuildPrompt(
        WebsiteProfile profile,
        ExecutiveSummaryData? executiveSummary,
        HistoricalScan? latestScan,
        List<AiSearchPrompt> prompts,
        List<CompetitorSnapshot> competitorSnapshots,
        List<VisibilityPlatformSnapshot> platformSnapshots)
    {
        const string systemPrompt =
            "You are a brand intelligence analyst reviewing how AI search engines portray a business. " +
            "Based ONLY on the real signals provided, respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"brandHealth\" (int 0-100), \"aiConfidence\" (int 0-100), \"messagingConsistency\" (int 0-100), \"brandTrust\" (int 0-100), " +
            "\"sentimentMix\": {\"positive\":int,\"neutral\":int,\"negative\":int} (must sum to 100), " +
            "\"alerts\": array of 2-3 {\"type\":\"risk\"|\"warning\"|\"win\",\"title\":string,\"message\":string}, " +
            "\"modelInsights\": array, one entry per platform listed below, of {\"platform\":string (must exactly match a listed platform name),\"sentiment\":\"pos\"|\"neu\"|\"neg\",\"themes\":array of 1-3 short theme strings,\"flag\":bool (true if this platform shows a notable accuracy or consistency concern)}, " +
            "\"accuracyFlags\": array of 2-4 {\"claim\":string,\"severity\":\"High\"|\"Medium\"|\"Low\",\"detail\":string,\"models\":array of platform name strings}, " +
            "\"promptEvidence\": array of 3-5 {\"prompt\":string (a realistic buyer question),\"sentiment\":\"pos\"|\"neu\"|\"neg\",\"sources\":array of 2-3 short source name strings}. " +
            "Ground every claim in the real business signals given — do not invent unrelated facts.";

        var sb = new StringBuilder();
        sb.AppendLine($"BUSINESS: {profile.BusinessName} ({profile.WebsiteUrl})");
        var rawProfile = profile.RawProfileJson;
        if (rawProfile.Length > 2000) rawProfile = rawProfile[..2000];
        sb.AppendLine($"WEBSITE PROFILE: {rawProfile}");

        if (executiveSummary != null)
        {
            sb.AppendLine($"EXECUTIVE SUMMARY: {executiveSummary.BusinessOverview}");
            sb.AppendLine($"Current AI visibility: {executiveSummary.CurrentAIVisibility}");
            sb.AppendLine($"Competitor position: {executiveSummary.CompetitorPosition}");
        }

        if (latestScan != null)
        {
            sb.AppendLine($"LATEST REAL SCAN SCORES: visibility {latestScan.VisibilityScore}, citation {latestScan.CitationScore}, sentiment {latestScan.SentimentScore}, competitor {latestScan.CompetitorScore}, hallucinationRisk {latestScan.HallucinationRisk}.");
        }

        if (prompts.Count > 0)
        {
            var avgBrand = prompts.Average(p => p.BrandStrength);
            var avgContent = prompts.Average(p => p.ContentStrength);
            var sample = prompts.Take(8).Select(p => p.QueryString);
            sb.AppendLine($"PROMPT SIGNALS: {prompts.Count} real prompts analyzed, avg brand strength {avgBrand:F0}, avg content strength {avgContent:F0}.");
            sb.AppendLine("Sample real prompts: " + string.Join(" | ", sample));
        }

        if (competitorSnapshots.Count > 0)
        {
            sb.AppendLine("REAL COMPETITIVE STANDING (share of voice): " +
                string.Join(", ", competitorSnapshots.OrderBy(c => c.Rank).Select(c => $"{c.Name}={c.ShareOfVoice}%")));
        }

        if (platformSnapshots.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"PLATFORMS TO PRODUCE modelInsights FOR (use these exact names, {platformSnapshots.Count} total):");
            foreach (var p in platformSnapshots)
            {
                sb.AppendLine($"- {p.Platform} (real visibility score: {p.Score}, status: {p.Status})");
            }
        }

        return (systemPrompt, sb.ToString());
    }

    private record ModelInsightJudged(string Platform, string Sentiment, List<string> Themes, bool Flag);

    private record JudgedResult(
        int BrandHealth, int AiConfidence, int MessagingConsistency, int BrandTrust,
        int SentimentPositive, int SentimentNeutral, int SentimentNegative,
        List<object> Alerts, List<ModelInsightJudged> ModelInsights, List<object> AccuracyFlags, List<object> PromptEvidence);

    private static JudgedResult ParseJudgment(string raw, HistoricalScan? latestScan)
    {
        var fallbackHealth = latestScan?.VisibilityScore ?? 50;
        var fallbackTrust = latestScan?.SentimentScore ?? 50;
        var fallbackConfidence = latestScan != null ? Math.Clamp(100 - latestScan.HallucinationRisk, 0, 100) : 50;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            int GetInt(JsonElement el, string key, int fallback) =>
                el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.TryGetInt32(out var iv) ? Math.Clamp(iv, 0, 100) : fallback;

            var brandHealth = GetInt(root, "brandHealth", fallbackHealth);
            var aiConfidence = GetInt(root, "aiConfidence", fallbackConfidence);
            var messagingConsistency = GetInt(root, "messagingConsistency", 60);
            var brandTrust = GetInt(root, "brandTrust", fallbackTrust);

            int posP = 55, neuP = 35, negP = 10;
            if (root.TryGetProperty("sentimentMix", out var sm) && sm.ValueKind == JsonValueKind.Object)
            {
                posP = GetInt(sm, "positive", posP);
                neuP = GetInt(sm, "neutral", neuP);
                negP = GetInt(sm, "negative", negP);
                var total = posP + neuP + negP;
                if (total > 0 && total != 100)
                {
                    posP = (int)Math.Round(posP / (double)total * 100);
                    neuP = (int)Math.Round(neuP / (double)total * 100);
                    negP = 100 - posP - neuP;
                }
            }

            List<object> ParseObjectArray(string key)
            {
                var list = new List<object>();
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        list.Add(JsonSerializer.Deserialize<object>(item.GetRawText())!);
                    }
                }
                return list;
            }

            var alerts = ParseObjectArray("alerts");
            var accuracyFlags = ParseObjectArray("accuracyFlags");
            var promptEvidence = ParseObjectArray("promptEvidence");

            var modelInsights = new List<ModelInsightJudged>();
            if (root.TryGetProperty("modelInsights", out var miArr) && miArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in miArr.EnumerateArray())
                {
                    var platform = item.TryGetProperty("platform", out var pn) ? pn.GetString() ?? "" : "";
                    var sentiment = item.TryGetProperty("sentiment", out var sv) ? sv.GetString() ?? "neu" : "neu";
                    var flag = item.TryGetProperty("flag", out var fv) && fv.ValueKind == JsonValueKind.True;
                    var themes = new List<string>();
                    if (item.TryGetProperty("themes", out var th) && th.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in th.EnumerateArray())
                        {
                            if (t.ValueKind == JsonValueKind.String) themes.Add(t.GetString() ?? "");
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(platform))
                        modelInsights.Add(new ModelInsightJudged(platform, sentiment, themes, flag));
                }
            }

            return new JudgedResult(brandHealth, aiConfidence, messagingConsistency, brandTrust, posP, neuP, negP, alerts, modelInsights, accuracyFlags, promptEvidence);
        }
        catch (Exception)
        {
            return new JudgedResult(
                fallbackHealth, fallbackConfidence, 60, fallbackTrust,
                55, 35, 10,
                new List<object>(), new List<ModelInsightJudged>(), new List<object>(), new List<object>());
        }
    }
}
