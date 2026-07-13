using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AnswerSimulatorController : ControllerBase
{
    private readonly IOpenAiService _openAiService;

    public AnswerSimulatorController(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    // ---------- Request DTOs ----------

    public class SimulateAnswerRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string Persona { get; set; } = string.Empty;
        public string Stage { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
    }

    public class CompareContentRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string PageContent { get; set; } = string.Empty;
    }

    public class BattleRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Competitor { get; set; } = string.Empty;
    }

    // ---------- Response DTOs ----------

    public class CompetitorShare
    {
        public string Name { get; set; } = string.Empty;
        public double SharePct { get; set; }
    }

    public class SourceReference
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "third";
    }

    public class SimulateAnswerResponse
    {
        public string Answer { get; set; } = string.Empty;
        public bool Mentioned { get; set; }
        public string Position { get; set; } = "Not mentioned";
        public string Sentiment { get; set; } = "neu";
        public double SharePct { get; set; }
        public List<CompetitorShare> Competitors { get; set; } = new();
        public List<SourceReference> Sources { get; set; } = new();
        public string Summary { get; set; } = string.Empty;
        public int ConsistencyOutOfFive { get; set; }
    }

    public class CompareContentResponse
    {
        public string Without { get; set; } = string.Empty;
        public string With { get; set; } = string.Empty;
        public bool Changed { get; set; }
        public string Verdict { get; set; } = string.Empty;
    }

    public class BattleResponse
    {
        public double YouPct { get; set; }
        public double CompPct { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    // ---------- AI-facing DTOs (case-insensitive-friendly for deserialization) ----------

    private class AiCompetitorShare
    {
        public string? name { get; set; }
        public double sharePct { get; set; }
    }

    private class AiSourceReference
    {
        public string? name { get; set; }
        public string? type { get; set; }
    }

    private class AiAnalysisResponse
    {
        public bool mentioned { get; set; }
        public string? position { get; set; }
        public string? sentiment { get; set; }
        public double sharePct { get; set; }
        public List<AiCompetitorShare>? competitors { get; set; }
        public List<AiSourceReference>? sources { get; set; }
        public string? summary { get; set; }
        public double consistencyOutOfFive { get; set; }
    }

    private class AiCompareResponse
    {
        public string? without { get; set; }
        public string? with { get; set; }
        public bool changed { get; set; }
        public string? verdict { get; set; }
    }

    private class AiBattleResponse
    {
        public double youPct { get; set; }
        public double compPct { get; set; }
        public string? note { get; set; }
    }

    // ---------- Endpoints ----------

    [HttpPost("simulate")]
    public async Task<IActionResult> Simulate([FromBody] SimulateAnswerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest("Prompt is required.");
        if (string.IsNullOrWhiteSpace(request.Brand))
            return BadRequest("Brand is required.");

        string answer;
        string analysisRaw;
        try
        {
            var answerSystemPrompt = $@"You are an AI search assistant answering for {request.Persona} who is {request.Stage}, in {request.Region}. " +
                "Give a concise, well-structured answer (120-170 words) naming specific real products/brands/sources where relevant, as a real AI search engine would.";

            answer = await _openAiService.GenerateContentAsync(
                prompt: request.Prompt,
                systemPrompt: answerSystemPrompt,
                requireJson: false,
                model: "gpt-4o-mini");

            var analysisSystemPrompt = "You are an expert brand visibility analyst. You analyze a given AI-generated answer text " +
                "and report how a specific brand fares within it. Return ONLY a raw JSON object, no markdown fences, no commentary.";

            var analysisUserPrompt = $@"Here is an AI search engine's answer to a user's question:

---
{answer}
---

Analyze how the brand ""{request.Brand}"" fares in this answer. Return a JSON object with EXACTLY this shape:
{{
  ""mentioned"": true/false (is ""{request.Brand}"" mentioned anywhere in the answer),
  ""position"": ""a short string describing where it ranks among named brands, e.g. '1st of 4' or 'Not mentioned'"",
  ""sentiment"": ""pos"" | ""neu"" | ""neg"" (overall sentiment toward ""{request.Brand}"" in the answer),
  ""sharePct"": 0-100 (estimated share of voice/attention ""{request.Brand}"" receives in the answer),
  ""competitors"": [ {{ ""name"": ""competitor or other named brand"", ""sharePct"": 0-100 }} ] (list every other named brand/product with its estimated share; all sharePct values including ""{request.Brand}""'s should sum to roughly 100),
  ""sources"": [ {{ ""name"": ""a source or reference implied or named in the answer"", ""type"": ""you"" | ""comp"" | ""third"" }} ] (""you"" = the brand ""{request.Brand}"" itself, ""comp"" = a competitor's own source, ""third"" = independent/third-party source),
  ""summary"": ""one short sentence summarizing {request.Brand}'s standing in this answer"",
  ""consistencyOutOfFive"": 1-5 (integer, how consistently/reliably this brand would likely appear across repeated similar queries)
}}

Return ONLY the JSON object.";

            analysisRaw = await _openAiService.GenerateContentAsync(
                prompt: analysisUserPrompt,
                systemPrompt: analysisSystemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        var parsed = TryParseAnalysis(analysisRaw);
        var result = new SimulateAnswerResponse
        {
            Answer = answer,
            Mentioned = parsed?.mentioned ?? false,
            Position = string.IsNullOrWhiteSpace(parsed?.position) ? "Not mentioned" : parsed!.position!,
            Sentiment = NormalizeSentiment(parsed?.sentiment),
            SharePct = parsed != null ? Math.Clamp(parsed.sharePct, 0, 100) : 0,
            Competitors = parsed?.competitors?.Select(c => new CompetitorShare
            {
                Name = c.name ?? "Competitor",
                SharePct = Math.Clamp(c.sharePct, 0, 100)
            }).ToList() ?? new List<CompetitorShare>(),
            Sources = parsed?.sources?.Select(s => new SourceReference
            {
                Name = s.name ?? "Source",
                Type = NormalizeSourceType(s.type)
            }).ToList() ?? new List<SourceReference>(),
            Summary = string.IsNullOrWhiteSpace(parsed?.summary)
                ? $"Could not fully analyze {request.Brand}'s standing in this answer."
                : parsed!.summary!,
            ConsistencyOutOfFive = parsed != null
                ? (int)Math.Clamp(Math.Round(parsed.consistencyOutOfFive), 1, 5)
                : 3
        };

        return Ok(result);
    }

    [HttpPost("compare")]
    public async Task<IActionResult> Compare([FromBody] CompareContentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest("Prompt is required.");
        if (string.IsNullOrWhiteSpace(request.Brand))
            return BadRequest("Brand is required.");

        var pageContent = request.PageContent ?? string.Empty;
        if (pageContent.Length > 1500)
            pageContent = pageContent.Substring(0, 1500);

        string raw;
        try
        {
            var systemPrompt = "You are an AI search assistant simulator. You produce two short simulated answers to a question: " +
                "one as if answered with no knowledge of a specific brand's content, and one as if that brand's content were available " +
                "as a retrievable source. Return ONLY a raw JSON object, no markdown fences, no commentary.";

            var userPrompt = $@"Question: {request.Prompt}

Brand: {request.Brand}

Brand's page content (may be truncated):
---
{pageContent}
---

Simulate two short AI answers (60-90 words each) to the question above:
1. ""without"": an answer WITHOUT considering the brand's content as a source (as if it didn't exist or wasn't indexed).
2. ""with"": an answer WITH the brand's content available as a source (naturally citing/using it where relevant).

Return a JSON object with EXACTLY this shape:
{{
  ""without"": ""60-90 word answer text"",
  ""with"": ""60-90 word answer text"",
  ""changed"": true/false (did having the content as a source meaningfully change the answer for ""{request.Brand}""),
  ""verdict"": ""one sentence on what changed for the brand ""{request.Brand}"" between the two answers""
}}

Return ONLY the JSON object.";

            raw = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        var parsed = TryParseCompare(raw);
        var result = parsed != null
            ? new CompareContentResponse
            {
                Without = string.IsNullOrWhiteSpace(parsed.without) ? "No answer could be generated." : parsed.without!,
                With = string.IsNullOrWhiteSpace(parsed.with) ? "No answer could be generated." : parsed.with!,
                Changed = parsed.changed,
                Verdict = string.IsNullOrWhiteSpace(parsed.verdict)
                    ? $"Unable to determine the exact impact on {request.Brand} right now."
                    : parsed.verdict!
            }
            : new CompareContentResponse
            {
                Without = "No answer could be generated.",
                With = "No answer could be generated.",
                Changed = false,
                Verdict = $"Unable to determine the exact impact on {request.Brand} right now."
            };

        return Ok(result);
    }

    [HttpPost("battle")]
    public async Task<IActionResult> Battle([FromBody] BattleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest("Prompt is required.");
        if (string.IsNullOrWhiteSpace(request.Brand))
            return BadRequest("Brand is required.");
        if (string.IsNullOrWhiteSpace(request.Competitor))
            return BadRequest("Competitor is required.");

        string raw;
        try
        {
            var systemPrompt = "You are an AI search visibility analyst estimating how answer share would shift between competing brands " +
                "under an adversarially-framed question. Return ONLY a raw JSON object, no markdown fences, no commentary.";

            var userPrompt = $@"Original question: {request.Prompt}

Brand: {request.Brand}
Rival brand: {request.Competitor}

Imagine the question were subtly framed to favor ""{request.Competitor}"" over ""{request.Brand}"". Estimate the share of the answer each brand would get in that scenario.

Return a JSON object with EXACTLY this shape:
{{
  ""youPct"": 0-100 (estimated share of the answer for ""{request.Brand}""),
  ""compPct"": 0-100 (estimated share of the answer for ""{request.Competitor}""),
  ""note"": ""one sentence on why the rival brand gains or loses ground in this framing""
}}

Return ONLY the JSON object.";

            raw = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }

        var parsed = TryParseBattle(raw);
        var result = parsed != null
            ? new BattleResponse
            {
                YouPct = Math.Clamp(parsed.youPct, 0, 100),
                CompPct = Math.Clamp(parsed.compPct, 0, 100),
                Note = string.IsNullOrWhiteSpace(parsed.note)
                    ? $"Unable to determine exactly how {request.Competitor} would perform against {request.Brand} in this framing."
                    : parsed.note!
            }
            : new BattleResponse
            {
                YouPct = 50,
                CompPct = 50,
                Note = $"Unable to determine exactly how {request.Competitor} would perform against {request.Brand} in this framing."
            };

        return Ok(result);
    }

    // ---------- Helpers ----------

    private static string NormalizeSentiment(string? sentiment)
    {
        var s = sentiment?.Trim().ToLowerInvariant();
        return s is "pos" or "neu" or "neg" ? s : "neu";
    }

    private static string NormalizeSourceType(string? type)
    {
        var t = type?.Trim().ToLowerInvariant();
        return t is "you" or "comp" or "third" ? t : "third";
    }

    private static string StripCodeFences(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
        }

        trimmed = trimmed.Trim();
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 3);
        }

        return trimmed.Trim();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private AiAnalysisResponse? TryParseAnalysis(string rawResponse)
    {
        try
        {
            var cleaned = StripCodeFences(rawResponse);
            return JsonSerializer.Deserialize<AiAnalysisResponse>(cleaned, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private AiCompareResponse? TryParseCompare(string rawResponse)
    {
        try
        {
            var cleaned = StripCodeFences(rawResponse);
            return JsonSerializer.Deserialize<AiCompareResponse>(cleaned, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private AiBattleResponse? TryParseBattle(string rawResponse)
    {
        try
        {
            var cleaned = StripCodeFences(rawResponse);
            return JsonSerializer.Deserialize<AiBattleResponse>(cleaned, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
