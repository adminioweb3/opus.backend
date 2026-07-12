using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Metrics;

public class RunScanCommand : IRequest<RunScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunScanResult(bool Success, string Message);

public class RunScanCommandHandler : IRequestHandler<RunScanCommand, RunScanResult>
{
    private static readonly string[] PillarKeys = { "answerReadiness", "schemaCoverage", "extractability", "freshness", "entityClarity", "authoritySignals" };
    private static readonly Dictionary<string, (string Label, string Description)> PillarInfo = new()
    {
        ["answerReadiness"] = ("Answer readiness", "Direct, liftable answers near the top of key pages"),
        ["schemaCoverage"] = ("Schema coverage", "Pages with FAQ / HowTo / Organization markup"),
        ["extractability"] = ("Extractability", "Scannable structure AI can quote as standalone answers"),
        ["freshness"] = ("Freshness", "Visible update dates and recently-touched content"),
        ["entityClarity"] = ("Entity clarity", "How unambiguously engines resolve who you are"),
        ["authoritySignals"] = ("Authority signals", "Sourced statistics, expert attribution, primary links"),
    };
    private static readonly string[] PromptTypes = { "Informational", "Commercial", "Comparison", "Transactional", "Local" };
    private static readonly string[] SovColors = { "#6366F1", "#16A34A", "#DB2777", "#F59E0B", "#8B5CF6", "#06B6D4", "#94A3B8" };

    private readonly IAiVisibilityRepository _aiVisibilityRepo;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;

    public RunScanCommandHandler(IAiVisibilityRepository aiVisibilityRepo, IWebsiteRepository websiteRepository, IOpenAiService openAiService)
    {
        _aiVisibilityRepo = aiVisibilityRepo;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
    }

    public async Task<RunScanResult> Handle(RunScanCommand request, CancellationToken cancellationToken)
    {
        await _aiVisibilityRepo.EnsureGeoTablesCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        var executiveSummary = await _websiteRepository.GetExecutiveSummaryAsync(orgId);
        var personaSummary = await _websiteRepository.GetPersonaAnalysisSummaryAsync(orgId);
        var regionSummary = await _websiteRepository.GetRegionAnalysisSummaryAsync(orgId);
        var competitors = await _aiVisibilityRepo.GetCompetitorsByOrgAsync(orgId);
        var previousScans = (await _aiVisibilityRepo.GetHistoricalScansByOrgAsync(orgId)).OrderBy(s => s.ScanDate).ToList();
        var previousScan = previousScans.LastOrDefault();

        if (profile == null && executiveSummary == null && personaSummary == null && regionSummary == null && competitors.Count == 0)
        {
            return new RunScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run a GEO scan.");
        }

        var (systemPrompt, userPrompt) = BuildPrompt(profile, executiveSummary, personaSummary, regionSummary, competitors, previousScan);
        var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
        var analysis = ParseAnalysis(raw, previousScan);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var scan = new HistoricalScan
        {
            OrganizationId = orgId,
            ScanDate = today,
            VisibilityScore = analysis.VisibilityScore,
            CitationScore = analysis.CitationScore,
            SentimentScore = analysis.SentimentScore,
            CompetitorScore = analysis.CompetitorScore,
            HallucinationRisk = analysis.HallucinationRisk,
            SeoHealth = analysis.SeoHealth,
            AeoReadiness = analysis.AeoReadiness,
            GeoReadiness = analysis.GeoReadiness
        };
        await _aiVisibilityRepo.InsertHistoricalScanAsync(scan);

        // ── Share of voice: computed deterministically from real competitor authority scores ──
        await _aiVisibilityRepo.DeleteShareOfVoiceByScanDateAsync(orgId, today);
        foreach (var sov in BuildShareOfVoice(orgId, today, analysis.VisibilityScore, competitors))
        {
            await _aiVisibilityRepo.InsertShareOfVoiceAsync(sov);
        }

        // ── Geo pillars ──
        foreach (var key in PillarKeys)
        {
            var (label, description) = PillarInfo[key];
            var score = analysis.Pillars.TryGetValue(key, out var pScore) ? pScore : 50;
            await _aiVisibilityRepo.InsertGeoPillarAsync(new GeoPillar
            {
                OrganizationId = orgId,
                ScanDate = today,
                PillarKey = key,
                Label = label,
                Description = description,
                Score = score
            });
        }

        // ── Prompt-type coverage ──
        foreach (var type in PromptTypes)
        {
            var coverage = analysis.PromptCoverage.TryGetValue(type, out var c) ? c : (Percentage: 50, Direction: "flat");
            await _aiVisibilityRepo.InsertPromptCoverageAsync(new PromptCoverage
            {
                OrganizationId = orgId,
                ScanDate = today,
                PromptType = type,
                Example = GetPromptExample(type),
                Note = $"{coverage.Percentage}% coverage this scan",
                Percentage = coverage.Percentage,
                Direction = coverage.Direction
            });
        }

        // ── Win/loss event: only logged if there's a genuine, meaningful score swing ──
        if (previousScan != null)
        {
            var visibilityDelta = analysis.VisibilityScore - previousScan.VisibilityScore;
            var citationDelta = analysis.CitationScore - previousScan.CitationScore;

            if (Math.Abs(visibilityDelta) >= 5 || Math.Abs(citationDelta) >= 5)
            {
                var isWin = (visibilityDelta + citationDelta) >= 0;
                var metric = Math.Abs(visibilityDelta) >= Math.Abs(citationDelta) ? "visibility" : "citation";
                var delta = metric == "visibility" ? visibilityDelta : citationDelta;

                await _aiVisibilityRepo.InsertWinLossEventAsync(new WinLossEvent
                {
                    OrganizationId = orgId,
                    Timestamp = DateTime.UtcNow,
                    Type = isWin ? "win" : "loss",
                    Engine = "GEO Scan",
                    Title = $"{metric switch { "visibility" => "Visibility", _ => "Citation" }} score {(delta >= 0 ? "improved" : "dropped")} by {Math.Abs(delta)} points"
                });
            }
        }

        return new RunScanResult(true, "GEO scan complete.");
    }

    private static string GetPromptExample(string type) => type switch
    {
        "Informational" => "\"what is / how does\"",
        "Commercial" => "\"best / top tools for\"",
        "Comparison" => "\"X vs Y\"",
        "Transactional" => "\"pricing / buy / trial\"",
        "Local" => "\"near me / in region\"",
        _ => ""
    };

    private static List<ShareOfVoice> BuildShareOfVoice(Guid orgId, DateOnly scanDate, int ownVisibility, List<Competitor> competitors)
    {
        var topCompetitors = competitors.OrderByDescending(c => c.Authority).Take(4).ToList();

        if (topCompetitors.Count == 0)
        {
            return new List<ShareOfVoice>
            {
                new() { OrganizationId = orgId, ScanDate = scanDate, CompetitorName = "Your Brand", SharePercentage = 100, ColorCode = SovColors[0] }
            };
        }

        var weights = new List<(string Name, int Weight, string Color)> { ("Your Brand", Math.Max(1, ownVisibility), SovColors[0]) };
        for (int i = 0; i < topCompetitors.Count; i++)
        {
            weights.Add((topCompetitors[i].Name, Math.Max(1, topCompetitors[i].Authority), SovColors[(i + 1) % SovColors.Length]));
        }

        var totalWeight = weights.Sum(w => w.Weight);
        var result = weights.Select(w => new ShareOfVoice
        {
            OrganizationId = orgId,
            ScanDate = scanDate,
            CompetitorName = w.Name,
            SharePercentage = (int)Math.Round((double)w.Weight / totalWeight * 100),
            ColorCode = w.Color
        }).ToList();

        // Rounding can drift the total slightly off 100 — correct it on the largest share.
        var drift = 100 - result.Sum(s => s.SharePercentage);
        if (drift != 0)
        {
            var largest = result.OrderByDescending(s => s.SharePercentage).First();
            largest.SharePercentage += drift;
        }

        return result;
    }

    private static (string SystemPrompt, string UserPrompt) BuildPrompt(
        WebsiteProfile? profile,
        ExecutiveSummaryData? executiveSummary,
        PersonaAnalysisSummary? personaSummary,
        RegionAnalysisSummary? regionSummary,
        List<Competitor> competitors,
        HistoricalScan? previousScan)
    {
        const string systemPrompt =
            "You are a GEO (Generative Engine Optimization) analyst. Based ONLY on the real business signals provided, " +
            "respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"visibilityScore\", \"citationScore\", \"sentimentScore\", \"competitorScore\", \"hallucinationRisk\", \"seoHealth\", \"aeoReadiness\", \"geoReadiness\" " +
            "(each an integer 0-100; for hallucinationRisk, lower means safer/better), " +
            "\"pillars\" (an object with integer 0-100 values for keys: answerReadiness, schemaCoverage, extractability, freshness, entityClarity, authoritySignals), " +
            "\"promptCoverage\" (an object where each key is one of Informational, Commercial, Comparison, Transactional, Local, and each value is " +
            "an object with \"percentage\" (integer 0-100) and \"direction\" (one of \"up\", \"down\", \"flat\")). " +
            "If a previous scan is provided, keep new scores realistically close to it (small, justified movement), not wildly different.";

        var sb = new StringBuilder();
        if (profile != null)
        {
            sb.AppendLine($"BUSINESS: {profile.BusinessName} ({profile.WebsiteUrl})");
            var rawProfile = profile.RawProfileJson;
            if (rawProfile.Length > 3000) rawProfile = rawProfile[..3000];
            sb.AppendLine($"WEBSITE PROFILE (from onboarding analysis): {rawProfile}");
        }
        if (executiveSummary != null)
        {
            sb.AppendLine($"\nEXECUTIVE SUMMARY: {executiveSummary.BusinessOverview}");
            sb.AppendLine($"Current AI visibility: {executiveSummary.CurrentAIVisibility}");
            sb.AppendLine($"Competitor position: {executiveSummary.CompetitorPosition}");
            sb.AppendLine($"Platform performance: {executiveSummary.PlatformPerformance}");
            sb.AppendLine($"Citation summary: {executiveSummary.CitationSummary}");
            sb.AppendLine($"Overall GEO score (prior analysis): {executiveSummary.OverallGEOScore}, AI visibility score: {executiveSummary.OverallAIVisibilityScore}, SEO score: {executiveSummary.OverallSEOScore}");
        }
        if (personaSummary != null)
        {
            sb.AppendLine($"\nPERSONA ANALYSIS: overall visibility {personaSummary.OverallVisibility}, strongest persona '{personaSummary.StrongestPersona}', weakest persona '{personaSummary.WeakestPersona}', average share of voice {personaSummary.AverageShareOfVoice}%.");
        }
        if (regionSummary != null)
        {
            sb.AppendLine($"\nREGION ANALYSIS: overall global visibility {regionSummary.OverallGlobalVisibility}, strongest region '{regionSummary.StrongestRegion}', weakest region '{regionSummary.WeakestRegion}', average share of voice {regionSummary.AverageShareOfVoice}%.");
        }
        if (competitors.Count > 0)
        {
            sb.AppendLine($"\nCOMPETITORS ({competitors.Count}): {string.Join(", ", competitors.Take(6).Select(c => $"{c.Name} (authority {c.Authority})"))}");
        }
        if (previousScan != null)
        {
            sb.AppendLine($"\nPREVIOUS SCAN: visibility {previousScan.VisibilityScore}, citation {previousScan.CitationScore}, sentiment {previousScan.SentimentScore}, competitor {previousScan.CompetitorScore}, hallucinationRisk {previousScan.HallucinationRisk}, seoHealth {previousScan.SeoHealth}, aeoReadiness {previousScan.AeoReadiness}, geoReadiness {previousScan.GeoReadiness}.");
        }

        return (systemPrompt, sb.ToString());
    }

    private record Analysis(
        int VisibilityScore, int CitationScore, int SentimentScore, int CompetitorScore,
        int HallucinationRisk, int SeoHealth, int AeoReadiness, int GeoReadiness,
        Dictionary<string, int> Pillars,
        Dictionary<string, (int Percentage, string Direction)> PromptCoverage);

    private static Analysis ParseAnalysis(string raw, HistoricalScan? previousScan)
    {
        int Fallback(Func<HistoricalScan, int> selector, int defaultValue) =>
            previousScan != null ? selector(previousScan) : defaultValue;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            int GetInt(string name, int fallback) =>
                root.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? Math.Clamp(v, 0, 100) : fallback;

            var pillars = new Dictionary<string, int>();
            if (root.TryGetProperty("pillars", out var pillarsEl) && pillarsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in PillarKeys)
                {
                    pillars[key] = pillarsEl.TryGetProperty(key, out var pEl) && pEl.TryGetInt32(out var pv) ? Math.Clamp(pv, 0, 100) : 50;
                }
            }

            var coverage = new Dictionary<string, (int, string)>();
            if (root.TryGetProperty("promptCoverage", out var coverageEl) && coverageEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var type in PromptTypes)
                {
                    if (coverageEl.TryGetProperty(type, out var cEl) && cEl.ValueKind == JsonValueKind.Object)
                    {
                        var pct = cEl.TryGetProperty("percentage", out var pctEl) && pctEl.TryGetInt32(out var pctV) ? Math.Clamp(pctV, 0, 100) : 50;
                        var dir = cEl.TryGetProperty("direction", out var dirEl) ? dirEl.GetString() ?? "flat" : "flat";
                        coverage[type] = (pct, dir);
                    }
                    else
                    {
                        coverage[type] = (50, "flat");
                    }
                }
            }

            return new Analysis(
                GetInt("visibilityScore", Fallback(s => s.VisibilityScore, 50)),
                GetInt("citationScore", Fallback(s => s.CitationScore, 50)),
                GetInt("sentimentScore", Fallback(s => s.SentimentScore, 50)),
                GetInt("competitorScore", Fallback(s => s.CompetitorScore, 50)),
                GetInt("hallucinationRisk", Fallback(s => s.HallucinationRisk, 20)),
                GetInt("seoHealth", Fallback(s => s.SeoHealth, 50)),
                GetInt("aeoReadiness", Fallback(s => s.AeoReadiness, 50)),
                GetInt("geoReadiness", Fallback(s => s.GeoReadiness, 50)),
                pillars,
                coverage);
        }
        catch (Exception)
        {
            var fallbackPillars = PillarKeys.ToDictionary(k => k, _ => 50);
            var fallbackCoverage = PromptTypes.ToDictionary(t => t, _ => (50, "flat"));
            return new Analysis(
                Fallback(s => s.VisibilityScore, 50), Fallback(s => s.CitationScore, 50),
                Fallback(s => s.SentimentScore, 50), Fallback(s => s.CompetitorScore, 50),
                Fallback(s => s.HallucinationRisk, 20), Fallback(s => s.SeoHealth, 50),
                Fallback(s => s.AeoReadiness, 50), Fallback(s => s.GeoReadiness, 50),
                fallbackPillars, fallbackCoverage);
        }
    }
}
