using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Competitors;

public class RunCompetitorScanCommand : IRequest<RunCompetitorScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunCompetitorScanResult(bool Success, string Message);

public class RunCompetitorScanCommandHandler : IRequestHandler<RunCompetitorScanCommand, RunCompetitorScanResult>
{
    private static readonly string[] ModelKeys = { "ChatGPT", "Claude", "Gemini", "Perplexity", "Copilot", "Grok" };
    private static readonly string[] Palette = { "#7C3AED", "#2563EB", "#14B8A6", "#D97706", "#DB2777" };

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorSnapshotRepository _snapshotRepo;
    private readonly IOpenAiService _openAiService;

    public RunCompetitorScanCommandHandler(
        IAiVisibilityRepository visibilityRepo,
        IWebsiteRepository websiteRepository,
        ICompetitorSnapshotRepository snapshotRepo,
        IOpenAiService openAiService)
    {
        _visibilityRepo = visibilityRepo;
        _websiteRepository = websiteRepository;
        _snapshotRepo = snapshotRepo;
        _openAiService = openAiService;
    }

    public async Task<RunCompetitorScanResult> Handle(RunCompetitorScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var competitors = await _visibilityRepo.GetCompetitorsByOrgAsync(orgId);
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(orgId);
        var scans = (await _visibilityRepo.GetHistoricalScansByOrgAsync(orgId)).OrderBy(s => s.ScanDate).ToList();
        var latestScan = scans.LastOrDefault();

        if (competitors.Count == 0 && profile == null && executiveSummary == null && latestScan == null)
        {
            return new RunCompetitorScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run a competitor scan.");
        }

        var previousScanDate = await _snapshotRepo.GetLatestScanDateAsync(orgId);
        var previousSnapshots = previousScanDate.HasValue
            ? await _snapshotRepo.GetSnapshotsByScanDateAsync(orgId, previousScanDate.Value)
            : new List<CompetitorSnapshot>();
        var previousYou = previousSnapshots.FirstOrDefault(s => s.IsYou);
        var previousByCompetitorId = previousSnapshots.Where(s => s.CompetitorId.HasValue).ToDictionary(s => s.CompetitorId!.Value);

        var (systemPrompt, userPrompt) = BuildPrompt(profile, executiveSummary, latestScan, competitors, previousYou, previousByCompetitorId);
        var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
        var judged = ParseJudgedScores(raw, competitors.Count, previousYou, previousByCompetitorId, competitors);

        // "You" uses the real, already-AI-analyzed VisibilityScore when available — the model's
        // own judged score only fills the gap when no GEO scan has run yet.
        var youScore = latestScan?.VisibilityScore ?? judged.You.Score;

        var entities = new List<(string Name, Guid? CompetitorId, bool IsYou, int Score, string Threat, Dictionary<string, int> ModelBreakdown, string? Tagline, string? WebsiteUrl)>
        {
            (BusinessName(profile), null, true, youScore, "low", judged.You.ModelBreakdown, Tagline(executiveSummary), profile?.WebsiteUrl)
        };

        for (int i = 0; i < competitors.Count; i++)
        {
            var comp = competitors[i];
            var j = judged.Competitors.Count > i ? judged.Competitors[i] : null;
            var score = j?.Score ?? previousByCompetitorId.GetValueOrDefault(comp.Id)?.Score ?? 50;
            var threat = j?.ThreatLevel ?? "low";
            var breakdown = j?.ModelBreakdown ?? ModelKeys.ToDictionary(k => k, _ => score);
            entities.Add((comp.Name, comp.Id, false, score, threat, breakdown, CompetitorTagline(comp), comp.WebsiteUrl));
        }

        var ranked = entities.OrderByDescending(e => e.Score).ToList();
        var totalWeight = ranked.Sum(e => Math.Max(1, e.Score));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        var sovAssigned = new List<int>();
        for (int i = 0; i < ranked.Count; i++)
        {
            var e = ranked[i];
            var sov = (int)Math.Round((double)Math.Max(1, e.Score) / totalWeight * 100);
            sovAssigned.Add(sov);
        }
        // Rounding drift correction, same convention as RunScanCommand.BuildShareOfVoice.
        var drift = 100 - sovAssigned.Sum();
        if (drift != 0 && sovAssigned.Count > 0)
        {
            var maxIdx = sovAssigned.IndexOf(sovAssigned.Max());
            sovAssigned[maxIdx] += drift;
        }

        for (int i = 0; i < ranked.Count; i++)
        {
            var e = ranked[i];
            var prev = e.IsYou ? previousYou : (e.CompetitorId.HasValue ? previousByCompetitorId.GetValueOrDefault(e.CompetitorId.Value) : null);
            var sov = sovAssigned[i];
            var sovChg = prev != null ? sov - prev.ShareOfVoice : 0;
            var visChg = prev != null ? e.Score - prev.Visibility : 0;

            await _snapshotRepo.InsertSnapshotAsync(new CompetitorSnapshot
            {
                OrganizationId = orgId,
                CompetitorId = e.CompetitorId,
                IsYou = e.IsYou,
                ScanDate = today,
                Name = e.Name,
                Score = e.Score,
                Rank = i + 1,
                ShareOfVoice = sov,
                ShareOfVoiceChange = sovChg,
                Visibility = e.Score,
                VisibilityChange = visChg,
                Threat = e.Threat,
                ModelsJson = JsonSerializer.Serialize(e.ModelBreakdown),
                Tagline = e.Tagline,
                WebsiteUrl = e.WebsiteUrl
            });
        }

        return new RunCompetitorScanResult(true, "Competitor scan complete.");
    }

    private static string BusinessName(WebsiteProfile? profile) =>
        string.IsNullOrWhiteSpace(profile?.BusinessName) ? "Your Brand" : profile!.BusinessName;

    private static string? Tagline(ExecutiveSummaryData? executiveSummary)
    {
        var overview = executiveSummary?.BusinessOverview;
        if (string.IsNullOrWhiteSpace(overview)) return "Your organization";
        return overview.Length > 90 ? overview[..90] : overview;
    }

    private static string CompetitorTagline(Competitor comp)
    {
        if (!string.IsNullOrWhiteSpace(comp.Description))
            return comp.Description.Length > 90 ? comp.Description[..90] : comp.Description;
        return string.IsNullOrWhiteSpace(comp.Industry) ? "Competitor" : comp.Industry;
    }

    private static (string SystemPrompt, string UserPrompt) BuildPrompt(
        WebsiteProfile? profile,
        ExecutiveSummaryData? executiveSummary,
        HistoricalScan? latestScan,
        List<Competitor> competitors,
        CompetitorSnapshot? previousYou,
        Dictionary<Guid, CompetitorSnapshot> previousByCompetitorId)
    {
        const string systemPrompt =
            "You are a competitive intelligence analyst. Based ONLY on the real business signals provided, " +
            "respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"you\": an object {\"score\": integer 0-100, \"modelBreakdown\": {\"ChatGPT\":int,\"Claude\":int,\"Gemini\":int,\"Perplexity\":int,\"Copilot\":int,\"Grok\":int} each 0-100}, " +
            "\"competitors\": an array in the EXACT SAME ORDER as the competitors listed below, one object per competitor: " +
            "{\"score\": integer 0-100, \"threatLevel\": \"low\"|\"med\"|\"high\", \"modelBreakdown\": {same 6 keys as above, each 0-100}}. " +
            "Score reflects overall competitive strength in AI-generated answers (citations, brand authority, visibility). " +
            "If previous scores are given for an entity, keep its new score realistically close to it (small, justified movement), not wildly different.";

        var sb = new StringBuilder();
        if (profile != null)
        {
            sb.AppendLine($"YOUR BUSINESS: {profile.BusinessName} ({profile.WebsiteUrl})");
            var rawProfile = profile.RawProfileJson;
            if (rawProfile.Length > 2000) rawProfile = rawProfile[..2000];
            sb.AppendLine($"YOUR WEBSITE PROFILE: {rawProfile}");
        }
        if (executiveSummary != null)
        {
            sb.AppendLine($"YOUR EXECUTIVE SUMMARY: {executiveSummary.BusinessOverview}");
            sb.AppendLine($"Your current AI visibility: {executiveSummary.CurrentAIVisibility}");
            sb.AppendLine($"Your competitor position: {executiveSummary.CompetitorPosition}");
        }
        if (latestScan != null)
        {
            sb.AppendLine($"YOUR REAL LATEST SCAN SCORES: visibility {latestScan.VisibilityScore}, citation {latestScan.CitationScore}, sentiment {latestScan.SentimentScore}, competitor {latestScan.CompetitorScore}, aeoReadiness {latestScan.AeoReadiness}, geoReadiness {latestScan.GeoReadiness}.");
        }
        if (previousYou != null)
        {
            sb.AppendLine($"YOUR PREVIOUS SCAN SCORE: {previousYou.Score}.");
        }

        sb.AppendLine();
        sb.AppendLine($"COMPETITORS ({competitors.Count}), in this exact order:");
        for (int i = 0; i < competitors.Count; i++)
        {
            var c = competitors[i];
            var line = new StringBuilder($"{i + 1}. {c.Name} — Industry: {c.Industry}");
            if (c.Authority > 0) line.Append($", Authority: {c.Authority}");
            if (!string.IsNullOrWhiteSpace(c.Description))
            {
                var desc = c.Description.Length > 200 ? c.Description[..200] : c.Description;
                line.Append($", Description: {desc}");
            }
            if (!string.IsNullOrWhiteSpace(c.EnrichedJson))
            {
                var enriched = c.EnrichedJson.Length > 300 ? c.EnrichedJson[..300] : c.EnrichedJson;
                line.Append($", Enriched data: {enriched}");
            }
            if (previousByCompetitorId.TryGetValue(c.Id, out var prevSnap))
            {
                line.Append($", Previous score: {prevSnap.Score}");
            }
            sb.AppendLine(line.ToString());
        }

        return (systemPrompt, sb.ToString());
    }

    private record JudgedYou(int Score, Dictionary<string, int> ModelBreakdown);
    private record JudgedCompetitor(int Score, string ThreatLevel, Dictionary<string, int> ModelBreakdown);
    private record JudgedResult(JudgedYou You, List<JudgedCompetitor> Competitors);

    private static JudgedResult ParseJudgedScores(
        string raw,
        int competitorCount,
        CompetitorSnapshot? previousYou,
        Dictionary<Guid, CompetitorSnapshot> previousByCompetitorId,
        List<Competitor> competitors)
    {
        Dictionary<string, int> DefaultBreakdown(int fallback) => ModelKeys.ToDictionary(k => k, _ => fallback);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            Dictionary<string, int> ParseBreakdown(JsonElement el, int fallback)
            {
                var result = new Dictionary<string, int>();
                foreach (var key in ModelKeys)
                {
                    result[key] = el.ValueKind == JsonValueKind.Object && el.TryGetProperty(key, out var v) && v.TryGetInt32(out var iv)
                        ? Math.Clamp(iv, 0, 100)
                        : fallback;
                }
                return result;
            }

            var youFallback = previousYou?.Score ?? 50;
            JudgedYou you;
            if (root.TryGetProperty("you", out var youEl) && youEl.ValueKind == JsonValueKind.Object)
            {
                var score = youEl.TryGetProperty("score", out var s) && s.TryGetInt32(out var sv) ? Math.Clamp(sv, 0, 100) : youFallback;
                var breakdown = youEl.TryGetProperty("modelBreakdown", out var mb) ? ParseBreakdown(mb, score) : DefaultBreakdown(score);
                you = new JudgedYou(score, breakdown);
            }
            else
            {
                you = new JudgedYou(youFallback, DefaultBreakdown(youFallback));
            }

            var comps = new List<JudgedCompetitor>();
            if (root.TryGetProperty("competitors", out var compsEl) && compsEl.ValueKind == JsonValueKind.Array)
            {
                var arr = compsEl.EnumerateArray().ToList();
                for (int i = 0; i < competitorCount; i++)
                {
                    var fallback = i < competitors.Count && previousByCompetitorId.TryGetValue(competitors[i].Id, out var prev)
                        ? prev.Score
                        : 50;

                    if (i < arr.Count && arr[i].ValueKind == JsonValueKind.Object)
                    {
                        var el = arr[i];
                        var score = el.TryGetProperty("score", out var s) && s.TryGetInt32(out var sv) ? Math.Clamp(sv, 0, 100) : fallback;
                        var threat = el.TryGetProperty("threatLevel", out var t) ? (t.GetString() ?? "low") : "low";
                        if (threat != "low" && threat != "med" && threat != "high") threat = "low";
                        var breakdown = el.TryGetProperty("modelBreakdown", out var mb) ? ParseBreakdown(mb, score) : DefaultBreakdown(score);
                        comps.Add(new JudgedCompetitor(score, threat, breakdown));
                    }
                    else
                    {
                        comps.Add(new JudgedCompetitor(fallback, "low", DefaultBreakdown(fallback)));
                    }
                }
            }
            else
            {
                for (int i = 0; i < competitorCount; i++)
                {
                    var fallback = i < competitors.Count && previousByCompetitorId.TryGetValue(competitors[i].Id, out var prev) ? prev.Score : 50;
                    comps.Add(new JudgedCompetitor(fallback, "low", DefaultBreakdown(fallback)));
                }
            }

            return new JudgedResult(you, comps);
        }
        catch (Exception)
        {
            var youFallback = previousYou?.Score ?? 50;
            var comps = new List<JudgedCompetitor>();
            for (int i = 0; i < competitorCount; i++)
            {
                var fallback = i < competitors.Count && previousByCompetitorId.TryGetValue(competitors[i].Id, out var prev) ? prev.Score : 50;
                comps.Add(new JudgedCompetitor(fallback, "low", DefaultBreakdown(fallback)));
            }
            return new JudgedResult(new JudgedYou(youFallback, DefaultBreakdown(youFallback)), comps);
        }
    }
}
