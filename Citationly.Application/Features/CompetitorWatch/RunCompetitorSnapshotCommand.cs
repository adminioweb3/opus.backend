using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.CompetitorWatch;

public class RunCompetitorSnapshotCommand : IRequest<RunCompetitorSnapshotResult>
{
    public Guid OrganizationId { get; set; }
}

public class RunCompetitorSnapshotResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

// -----------------------------------------------------------------------
// Response DTOs consumed directly by DashboardController (Ok(dto) — no envelope).
// PascalCase here becomes camelCase in the JSON payload, matching the frontend's
// CompetitorWatchResponse / CompetitorWatchItem TypeScript shapes exactly.
// -----------------------------------------------------------------------

public class CompetitorWatchResponse
{
    public CompetitorWatchItem? You { get; set; }
    public List<CompetitorWatchItem> Comps { get; set; } = new();
}

public class CompetitorWatchItem
{
    public string Id { get; set; } = string.Empty;
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Tagline { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool You { get; set; }
    public int Sov { get; set; }
    public int SovChg { get; set; }
    public int Vis { get; set; }
    public int VisChg { get; set; }
    public string Threat { get; set; } = "low";
    public List<int> Trend { get; set; } = new();
    public CompetitorWatchCitations Citations { get; set; } = new();
    public CompetitorWatchContent Content { get; set; } = new();
    public Dictionary<string, int> Models { get; set; } = new();
}

public class CompetitorWatchCitations
{
    public int Share { get; set; }
    public int Total { get; set; }
}

public class CompetitorWatchContent
{
    public string Velocity { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------
// Handler
// -----------------------------------------------------------------------

public class RunCompetitorSnapshotCommandHandler : IRequestHandler<RunCompetitorSnapshotCommand, RunCompetitorSnapshotResult>
{
    private readonly ICompetitorSnapshotRepository _snapshotRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openAiService;

    public RunCompetitorSnapshotCommandHandler(
        ICompetitorSnapshotRepository snapshotRepository,
        IWebsiteRepository websiteRepository,
        IOpenAiService openAiService)
    {
        _snapshotRepository = snapshotRepository;
        _websiteRepository = websiteRepository;
        _openAiService = openAiService;
    }

    public async Task<RunCompetitorSnapshotResult> Handle(RunCompetitorSnapshotCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var organizationId = request.OrganizationId;

            var competitors = (await _websiteRepository.GetCompetitorsAsync(organizationId))?.ToList()
                               ?? new List<Competitor>();
            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);

            var businessName = string.IsNullOrWhiteSpace(profile?.BusinessName) ? "Your Business" : profile!.BusinessName;
            var websiteUrl = profile?.WebsiteUrl ?? string.Empty;
            var industry = ExtractIndustry(profile?.RawProfileJson);

            var aiResult = await RunAiAnalysisAsync(businessName, websiteUrl, industry, competitors);

            // ---- Build the "you" row (defensive fallback to neutral values) ----
            var youAi = aiResult?.You;
            var youSnapshot = new CompetitorSnapshot
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CompetitorId = null,
                IsYou = true,
                Name = businessName,
                Score = ClampScore(youAi?.Score, 50),
                ShareOfVoice = ClampScore(youAi?.ShareOfVoice, 30),
                ShareOfVoiceChange = youAi?.ShareOfVoiceChange ?? 0,
                Visibility = ClampScore(youAi?.Visibility, 50),
                VisibilityChange = youAi?.VisibilityChange ?? 0,
                Threat = "n/a",
                ModelsJson = SerializeModels(youAi?.ModelsPerf),
                Tagline = string.IsNullOrWhiteSpace(youAi?.Tagline)
                    ? "Your business's current AI-answer-engine visibility snapshot."
                    : youAi!.Tagline!,
                WebsiteUrl = websiteUrl,
                CitationsShare = ClampScore(youAi?.CitationsShare, 20),
                CitationsTotal = Math.Max(0, youAi?.CitationsTotal ?? 0),
                ContentVelocity = string.IsNullOrWhiteSpace(youAi?.ContentVelocity) ? "N/A" : youAi!.ContentVelocity!
            };

            // ---- Build competitor rows, matched to the AI response by position, falling back to name ----
            var aiCompetitors = aiResult?.Competitors ?? new List<AiSnapshotEntity>();
            var competitorSnapshots = new List<CompetitorSnapshot>();

            for (var i = 0; i < competitors.Count; i++)
            {
                var comp = competitors[i];
                AiSnapshotEntity? match = i < aiCompetitors.Count ? aiCompetitors[i] : null;
                if (match == null || (!string.IsNullOrWhiteSpace(match.Name) && !NamesRoughlyMatch(match.Name, comp.Name)))
                {
                    match = aiCompetitors.FirstOrDefault(a => NamesRoughlyMatch(a.Name, comp.Name)) ?? match;
                }

                competitorSnapshots.Add(new CompetitorSnapshot
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    CompetitorId = comp.Id,
                    IsYou = false,
                    Name = comp.Name,
                    Score = ClampScore(match?.Score, 40),
                    ShareOfVoice = ClampScore(match?.ShareOfVoice, 10),
                    ShareOfVoiceChange = match?.ShareOfVoiceChange ?? 0,
                    Visibility = ClampScore(match?.Visibility, 40),
                    VisibilityChange = match?.VisibilityChange ?? 0,
                    Threat = NormalizeThreat(match?.Threat),
                    ModelsJson = SerializeModels(match?.ModelsPerf),
                    Tagline = string.IsNullOrWhiteSpace(match?.Tagline) ? $"{comp.Name} — tracked competitor." : match!.Tagline!,
                    WebsiteUrl = string.IsNullOrWhiteSpace(comp.WebsiteUrl) ? null : comp.WebsiteUrl,
                    CitationsShare = ClampScore(match?.CitationsShare, 10),
                    CitationsTotal = Math.Max(0, match?.CitationsTotal ?? 0),
                    ContentVelocity = string.IsNullOrWhiteSpace(match?.ContentVelocity) ? "N/A" : match!.ContentVelocity!
                });
            }

            // ---- Rank everyone (you + competitors) by Score descending ----
            var all = new List<CompetitorSnapshot> { youSnapshot };
            all.AddRange(competitorSnapshots);
            var ranked = all.OrderByDescending(s => s.Score).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                ranked[i].Rank = i + 1;
            }

            await _snapshotRepository.SaveScanAsync(organizationId, youSnapshot, competitorSnapshots);

            return new RunCompetitorSnapshotResult { Success = true };
        }
        catch (Exception ex)
        {
            return new RunCompetitorSnapshotResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<AiSnapshotResponse?> RunAiAnalysisAsync(
        string businessName, string websiteUrl, string industry, List<Competitor> competitors)
    {
        var competitorInputs = competitors.Select(c => new { name = c.Name, domain = c.WebsiteUrl }).ToList();

        var systemPrompt =
            "You are an expert competitive intelligence and AI-search visibility analyst who benchmarks brands " +
            "against their competitors across AI answer engines (ChatGPT, Claude, Gemini, Perplexity).";

        var userPrompt = $@"Business context:
Business Name: {businessName}
Website: {websiteUrl}
Industry: {industry}

Real tracked competitors (name/domain), in this exact order:
{JsonSerializer.Serialize(competitorInputs)}

Task: Benchmark ""you"" (the business above) against each listed competitor for AI-answer-engine visibility and
overall share-of-voice in AI-generated answers.

Return ONLY a single JSON object with this EXACT schema — no markdown, no code fences, no commentary:
{{
  ""you"": {{
    ""score"": 0-100,
    ""shareOfVoice"": 0-100,
    ""shareOfVoiceChange"": signed integer,
    ""visibility"": 0-100,
    ""visibilityChange"": signed integer,
    ""citationsShare"": 0-100,
    ""citationsTotal"": integer,
    ""contentVelocity"": ""short string, e.g. '3 pages/week'"",
    ""tagline"": ""one short positioning line"",
    ""modelsPerf"": {{ ""ChatGPT"": 0-100, ""Claude"": 0-100, ""Gemini"": 0-100, ""Perplexity"": 0-100 }}
  }},
  ""competitors"": [
    {{
      ""name"": ""must exactly match one of the provided competitor names, same order as given"",
      ""score"": 0-100,
      ""shareOfVoice"": 0-100,
      ""shareOfVoiceChange"": signed integer,
      ""visibility"": 0-100,
      ""visibilityChange"": signed integer,
      ""threat"": ""high"" | ""med"" | ""low"",
      ""citationsShare"": 0-100,
      ""citationsTotal"": integer,
      ""contentVelocity"": ""short string, e.g. '2 pages/week'"",
      ""tagline"": ""one short positioning line"",
      ""modelsPerf"": {{ ""ChatGPT"": 0-100, ""Claude"": 0-100, ""Gemini"": 0-100, ""Perplexity"": 0-100 }}
    }}
  ]
}}

Rules:
- ""score"" is the overall AI-answer performance for that entity (0-100).
- shareOfVoice across you + all competitors should roughly sum to 100.
- Every listed competitor must appear exactly once in the competitors array, in the same order they were given.
- If no competitors were provided, return an empty competitors array.
- Return ONLY the JSON object described above.";

        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true, model: "gpt-4o-mini");
            var cleaned = CleanJson(raw);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            return JsonSerializer.Deserialize<AiSnapshotResponse>(cleaned, options);
        }
        catch
        {
            // Defensive: malformed/failed AI response never throws — caller falls back to neutral defaults.
            return null;
        }
    }

    private static string ExtractIndustry(string? rawProfileJson)
    {
        if (string.IsNullOrWhiteSpace(rawProfileJson))
        {
            return "General";
        }

        try
        {
            using var doc = JsonDocument.Parse(rawProfileJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return "General";
            }

            // The onboarding profile stores industry info nested as
            // "industriesServed": { "value": ["..."], "confidence": 0 } (see AnalyzeOnboardingCommand's schema),
            // not as a flat "industry" string — read the nested shape, with a couple of defensive fallbacks.
            if (doc.RootElement.TryGetProperty("industriesServed", out var industriesEl) &&
                industriesEl.ValueKind == JsonValueKind.Object &&
                industriesEl.TryGetProperty("value", out var valueEl))
            {
                if (valueEl.ValueKind == JsonValueKind.Array)
                {
                    var first = valueEl.EnumerateArray().FirstOrDefault(e => e.ValueKind == JsonValueKind.String);
                    var arrValue = first.ValueKind == JsonValueKind.String ? first.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(arrValue))
                    {
                        return arrValue!;
                    }
                }
                else if (valueEl.ValueKind == JsonValueKind.String)
                {
                    var strValue = valueEl.GetString();
                    if (!string.IsNullOrWhiteSpace(strValue))
                    {
                        return strValue!;
                    }
                }
            }

            // Defensive fallback for any other shape that might carry a flat "industry" string.
            if (doc.RootElement.TryGetProperty("industry", out var industryEl) &&
                industryEl.ValueKind == JsonValueKind.String)
            {
                var value = industryEl.GetString();
                return string.IsNullOrWhiteSpace(value) ? "General" : value!;
            }
        }
        catch
        {
            // ignore malformed profile JSON, fall through to default
        }

        return "General";
    }

    private static bool NamesRoughlyMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int ClampScore(int? value, int fallback)
    {
        var v = value ?? fallback;
        return Math.Max(0, Math.Min(100, v));
    }

    private static string NormalizeThreat(string? threat)
    {
        var t = threat?.Trim().ToLowerInvariant();
        return t switch
        {
            "high" => "high",
            "med" => "med",
            "medium" => "med",
            "low" => "low",
            _ => "low"
        };
    }

    private static string SerializeModels(Dictionary<string, int>? models)
    {
        var dict = models != null ? new Dictionary<string, int>(models) : new Dictionary<string, int>();
        if (!dict.ContainsKey("ChatGPT")) dict["ChatGPT"] = 50;
        if (!dict.ContainsKey("Claude")) dict["Claude"] = 50;
        if (!dict.ContainsKey("Gemini")) dict["Gemini"] = 50;
        if (!dict.ContainsKey("Perplexity")) dict["Perplexity"] = 50;
        return JsonSerializer.Serialize(dict);
    }

    private static string CleanJson(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7);
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3);
        }
        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 3);
        }
        return cleaned.Trim();
    }

    // -------------------------------------------------------------------
    // AI response parsing DTOs (internal, not exposed to controllers).
    // -------------------------------------------------------------------

    private class AiSnapshotResponse
    {
        public AiSnapshotEntity? You { get; set; }
        public List<AiSnapshotEntity>? Competitors { get; set; }
    }

    private class AiSnapshotEntity
    {
        public string? Name { get; set; }
        public int? Score { get; set; }
        public int? ShareOfVoice { get; set; }
        public int? ShareOfVoiceChange { get; set; }
        public int? Visibility { get; set; }
        public int? VisibilityChange { get; set; }
        public string? Threat { get; set; }
        public int? CitationsShare { get; set; }
        public int? CitationsTotal { get; set; }
        public string? ContentVelocity { get; set; }
        public string? Tagline { get; set; }
        public Dictionary<string, int>? ModelsPerf { get; set; }
    }
}
