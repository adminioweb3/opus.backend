using System.Text;
using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Application.Interfaces.GeoAudit;
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

    /// <summary>How far back to pull real prompt-intelligence data for the Phase 3 A1 real scores.</summary>
    private const int RealScoreLookbackDays = 30;

    private readonly IAiVisibilityRepository _aiVisibilityRepo;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IPromptIntelligenceRepository _promptIntelligenceRepo;
    private readonly ICompetitorRankingService _competitorRankingService;
    private readonly IGeoTechnicalAuditService _geoTechnicalAuditService;

    public RunScanCommandHandler(
        IAiVisibilityRepository aiVisibilityRepo,
        IWebsiteRepository websiteRepository,
        IAiCompletionService aiCompletionService,
        IPromptIntelligenceRepository promptIntelligenceRepo,
        ICompetitorRankingService competitorRankingService,
        IGeoTechnicalAuditService geoTechnicalAuditService)
    {
        _aiVisibilityRepo = aiVisibilityRepo;
        _websiteRepository = websiteRepository;
        _aiCompletionService = aiCompletionService;
        _promptIntelligenceRepo = promptIntelligenceRepo;
        _competitorRankingService = competitorRankingService;
        _geoTechnicalAuditService = geoTechnicalAuditService;
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

        // Phase 3 A1: Visibility/Citation/Sentiment/Competitor are now real, deterministic
        // computations over the org's actual prompt-intelligence history instead of a single LLM
        // call inventing all 8 scores at once. Only the four that still have no real data source
        // (pending Phase 4's technical GEO audit and Phase 5's fact-accuracy monitor) go to the
        // model — and it is no longer told to "keep scores close to the previous scan", since that
        // instruction only ever existed to paper over the fact that nothing was really measured.
        var since = DateTime.UtcNow.AddDays(-RealScoreLookbackDays);
        var realVisibilityScore = await ComputeRealVisibilityScoreAsync(orgId, since);
        var realCitationScore = await ComputeRealCitationScoreAsync(orgId, since);
        var realSentimentScore = await ComputeRealSentimentScoreAsync(orgId, since);
        var realCompetitorScore = await ComputeRealCompetitorScoreAsync(orgId, cancellationToken);
        var geoAudit = profile == null
            ? null
            : await _geoTechnicalAuditService.AuditAsync(profile.WebsiteUrl, cancellationToken);

        var (systemPrompt, userPrompt) = BuildPrompt(profile, executiveSummary, personaSummary, regionSummary, competitors, previousScan);
        var completion = await _aiCompletionService.CompleteAsync(
            orgId,
            "geo.scan.technical_estimates",
            userPrompt,
            systemPrompt,
            requireJson: true,
            preferredProviderKey: "openai",
            cancellationToken);
        if (!completion.Success)
        {
            return new RunScanResult(false, completion.ErrorMessage ?? "GEO scan could not be completed because AI estimates were unavailable.");
        }

        var analysis = ParseAnalysis(completion.Content, previousScan);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var scan = new HistoricalScan
        {
            OrganizationId = orgId,
            ScanDate = today,
            VisibilityScore = realVisibilityScore,
            CitationScore = realCitationScore,
            SentimentScore = realSentimentScore,
            CompetitorScore = realCompetitorScore,
            HallucinationRisk = analysis.HallucinationRisk,
            SeoHealth = geoAudit?.SeoHealthScore ?? analysis.SeoHealth,
            AeoReadiness = geoAudit?.AeoReadinessScore ?? analysis.AeoReadiness,
            GeoReadiness = geoAudit?.OverallScore ?? analysis.GeoReadiness,
            ScoringMethodVersion = geoAudit == null ? "v2-partial-real" : "v3-geo-audit",
        };
        await _aiVisibilityRepo.InsertHistoricalScanAsync(scan);

        // ── Share of voice: computed deterministically from real competitor authority scores ──
        await _aiVisibilityRepo.DeleteShareOfVoiceByScanDateAsync(orgId, today);
        foreach (var sov in BuildShareOfVoice(orgId, today, realVisibilityScore, competitors))
        {
            await _aiVisibilityRepo.InsertShareOfVoiceAsync(sov);
        }

        // ── Geo pillars ──
        foreach (var key in PillarKeys)
        {
            var (label, description) = PillarInfo[key];
            var score = geoAudit?.PillarScores.GetValueOrDefault(key)
                ?? (analysis.Pillars.TryGetValue(key, out var pScore) ? pScore : 50);
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
            var visibilityDelta = realVisibilityScore - previousScan.VisibilityScore;
            var citationDelta = realCitationScore - previousScan.CitationScore;

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
        // Phase 3 A1/A3: this prompt used to also ask for visibilityScore/citationScore/
        // sentimentScore/competitorScore and was told to "keep new scores realistically close to"
        // the previous scan — an instruction that existed only to make wholly-invented numbers
        // look like a believable trend. Those four are now computed deterministically from real
        // data (see ComputeReal*ScoreAsync below) before this prompt is even built. What remains
        // here (hallucination risk, SEO/AEO/GEO readiness, pillars, prompt coverage) has no real
        // data source yet — that requires Phase 4's technical GEO audit and Phase 5's
        // fact-accuracy monitor — so it is still an LLM estimate, not a fabricated-but-labeled-
        // real score. previousScan is shown for context only, not as an anchor to match.
        const string systemPrompt =
            "You are a GEO (Generative Engine Optimization) analyst estimating technical readiness signals " +
            "that cannot yet be measured directly. Based ONLY on the real business signals provided, " +
            "respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"hallucinationRisk\", \"seoHealth\", \"aeoReadiness\", \"geoReadiness\" " +
            "(each an integer 0-100; for hallucinationRisk, lower means safer/better), " +
            "\"pillars\" (an object with integer 0-100 values for keys: answerReadiness, schemaCoverage, extractability, freshness, entityClarity, authoritySignals), " +
            "\"promptCoverage\" (an object where each key is one of Informational, Commercial, Comparison, Transactional, Local, and each value is " +
            "an object with \"percentage\" (integer 0-100) and \"direction\" (one of \"up\", \"down\", \"flat\")). " +
            "Give your best independent estimate for each field based on the evidence provided — do not anchor to any previous scan shown below.";

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
                Fallback(s => s.HallucinationRisk, 20), Fallback(s => s.SeoHealth, 50),
                Fallback(s => s.AeoReadiness, 50), Fallback(s => s.GeoReadiness, 50),
                fallbackPillars, fallbackCoverage);
        }
    }

    // ── Phase 3 A1: real, deterministic score computation ──────────────────────────────────

    /// <summary>Average of OverallVisibilityScore across the org's real prompt-intelligence
    /// executions (VisibilityCalculatorService's deterministic formula) in the lookback window.
    /// 50 (neutral, not a guess) if the org hasn't run any prompts yet.</summary>
    private async Task<int> ComputeRealVisibilityScoreAsync(Guid organizationId, DateTime since)
    {
        var rows = (await _promptIntelligenceRepo.GetVisibilitySummaryDataAsync(organizationId, since)).ToList();
        return rows.Count == 0 ? 50 : (int)Math.Round(rows.Average(r => r.OverallVisibilityScore));
    }

    /// <summary>Real "citation coverage": the percentage of the org's captured AI-response
    /// analyses that included at least one citation to the org's own ("Owned") domain, per
    /// CitationExtractorService's real regex extraction. 50 if there's no analysis history yet.</summary>
    private async Task<int> ComputeRealCitationScoreAsync(Guid organizationId, DateTime since)
    {
        var visibilityRows = (await _promptIntelligenceRepo.GetVisibilitySummaryDataAsync(organizationId, since)).ToList();
        if (visibilityRows.Count == 0) return 50;

        var citationRows = (await _promptIntelligenceRepo.GetCitationSummaryDataAsync(organizationId, since)).ToList();
        var analysesWithOwnedCitation = citationRows
            .Where(c => c.Category == "Owned")
            .Select(c => c.AnalysisId)
            .Distinct()
            .Count();

        return (int)Math.Round(Math.Min(1.0, analysesWithOwnedCitation / (double)visibilityRows.Count) * 100);
    }

    /// <summary>Real net-sentiment score from SentimentClassifierService's per-response
    /// classifications: 50 (neutral) plus a swing toward 100/0 based on the positive-minus-
    /// negative share of classified responses. 50 if nothing has been classified yet.</summary>
    private async Task<int> ComputeRealSentimentScoreAsync(Guid organizationId, DateTime since)
    {
        var rows = (await _promptIntelligenceRepo.GetSentimentSummaryDataAsync(organizationId, since))
            .Where(r => !string.IsNullOrWhiteSpace(r.Sentiment))
            .ToList();
        if (rows.Count == 0) return 50;

        var positive = rows.Count(r => r.Sentiment == "pos");
        var negative = rows.Count(r => r.Sentiment == "neg");
        return (int)Math.Round(Math.Clamp(50 + (positive - negative) * 50.0 / rows.Count, 0, 100));
    }

    /// <summary>Real competitive percentile from CompetitorRankingService's deterministic,
    /// zero-AI-call ranking engine. Falls back to a documented neutral default (not a guess) if
    /// the org has no comparable competitors yet, or if ranking otherwise can't be computed.</summary>
    private async Task<int> ComputeRealCompetitorScoreAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            var ranking = await _competitorRankingService.ComputeRankingsAsync(organizationId, cancellationToken);
            return ranking.TotalCompanies <= 1 ? 50 : (int)Math.Round(Math.Clamp(ranking.Percentile, 0, 100));
        }
        catch
        {
            return 50;
        }
    }
}
