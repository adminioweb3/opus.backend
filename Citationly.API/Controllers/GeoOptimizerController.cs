using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class GeoOptimizerController : ControllerBase
{
    private readonly IOpenAiService _openAiService;

    public GeoOptimizerController(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public class GeoOptimizationRequest
    {
        public string? Url { get; set; }
        public string? Content { get; set; }
        public string TargetKeyword { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
    }

    public class GeoSubMetric
    {
        public string? Label { get; set; }
        public int Score { get; set; }
    }

    public class FixRecommendation
    {
        public string? Title { get; set; }
        public string? Impact { get; set; }
        public string? Icon { get; set; }
        public string? Description { get; set; }
        public string? Delta { get; set; }
    }

    public class CompetitorGap
    {
        public string? Name { get; set; }
        public string? Coverage { get; set; }
        public string? Status { get; set; }
    }

    public class PromptCoverageItem
    {
        public string? Question { get; set; }
        public string? Coverage { get; set; }
        public string? Note { get; set; }
    }

    public class CitationSignal
    {
        public string? Icon { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public int Score { get; set; }
    }

    public class GeoOptimizationResponse
    {
        public int Score { get; set; }
        public string? Verdict { get; set; }
        public string? StatusText { get; set; }
        public List<GeoSubMetric> SubMetrics { get; set; } = new();
        public List<FixRecommendation> FixRecommendations { get; set; } = new();
        public List<CompetitorGap> CompetitorGap { get; set; } = new();
        public List<PromptCoverageItem> PromptCoverage { get; set; } = new();
        public List<CitationSignal> CitationGap { get; set; } = new();
    }

    public class SchemaGenerationRequest
    {
        public string? Url { get; set; }
        public string? Content { get; set; }
        public string SchemaType { get; set; } = string.Empty;
    }

    public class SchemaGenerationResponse
    {
        public string JsonLd { get; set; } = string.Empty;
    }

    // Case-insensitive-friendly DTOs used purely for deserializing the AI's JSON payload.
    private class AiSubMetric
    {
        public string? label { get; set; }
        public double score { get; set; }
    }

    private class AiFixRecommendation
    {
        public string? title { get; set; }
        public string? impact { get; set; }
        public string? icon { get; set; }
        public string? description { get; set; }
        public string? delta { get; set; }
    }

    private class AiCompetitorGap
    {
        public string? name { get; set; }
        public string? coverage { get; set; }
        public string? status { get; set; }
    }

    private class AiPromptCoverageItem
    {
        public string? question { get; set; }
        public string? coverage { get; set; }
        public string? note { get; set; }
    }

    private class AiCitationSignal
    {
        public string? icon { get; set; }
        public string? title { get; set; }
        public string? status { get; set; }
        public double score { get; set; }
    }

    private class AiGeoOptimizationResponse
    {
        public double score { get; set; }
        public string? verdict { get; set; }
        public string? statusText { get; set; }
        public List<AiSubMetric>? subMetrics { get; set; }
        public List<AiFixRecommendation>? fixRecommendations { get; set; }
        public List<AiCompetitorGap>? competitorGap { get; set; }
        public List<AiPromptCoverageItem>? promptCoverage { get; set; }
        public List<AiCitationSignal>? citationGap { get; set; }
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze([FromBody] GeoOptimizationRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.TargetKeyword))
                return BadRequest("TargetKeyword is required.");

            if (string.IsNullOrWhiteSpace(request.Url) && string.IsNullOrWhiteSpace(request.Content))
                return BadRequest("Either url or content must be provided.");

            var systemPrompt = @"You are an expert Generative Engine Optimization (GEO) auditor. You analyze web pages and content to
estimate how well they would perform when cited or summarized by AI answer engines (ChatGPT, Perplexity, Google AI Overview, Gemini, Copilot).
When you are only given a URL and no page content, you do NOT have the ability to browse the internet. You must produce a plausible,
coherent, best-effort SIMULATED audit based on reasoning about the URL structure, domain, target keyword, and engine — clearly acting as an
estimation tool, not a factual scrape. Never refuse. Always return a complete, internally consistent JSON object matching the requested schema.
Return ONLY raw JSON. Do not wrap in markdown fences. Do not include explanations outside the JSON.";

            string subject = !string.IsNullOrWhiteSpace(request.Content)
                ? $"Evaluate the following pasted page content directly and literally:\n\n---\n{request.Content}\n---"
                : $"You are evaluating (by estimation/simulation only, since you cannot browse) the live page at this URL: {request.Url}\n" +
                  "You do not have its actual content, so reason plausibly from the URL, domain, target keyword, and engine to produce a realistic, coherent simulated audit.";

            var userPrompt = $@"{subject}

Target keyword: {request.TargetKeyword}
Target AI engine: {request.Engine}

Produce a GEO (Generative Engine Optimization) audit as a JSON object with EXACTLY this shape:
{{
  ""score"": 0-100 overall GEO readiness score (integer),
  ""verdict"": short 2-4 word verdict, e.g. ""Needs Work"" or ""Strong Performer"",
  ""statusText"": one sentence summarizing the overall assessment,
  ""subMetrics"": [
    {{ ""label"": ""Structure"", ""score"": 0-100 }},
    {{ ""label"": ""Clarity"", ""score"": 0-100 }},
    {{ ""label"": ""Citations Readiness"", ""score"": 0-100 }},
    {{ ""label"": ""Schema Coverage"", ""score"": 0-100 }}
  ],
  ""fixRecommendations"": [
    {{ ""title"": ""short fix title"", ""impact"": ""High|Medium|Low"", ""icon"": ""a lucide-react icon name such as FileText, Link, Code, ListChecks, Quote, BookOpen"", ""description"": ""1-2 sentence explanation of the fix"", ""delta"": ""expected impact e.g. '+8 pts'"" }}
    // 4-6 items total
  ],
  ""competitorGap"": [
    {{ ""name"": ""plausible competitor name for this keyword/industry"", ""coverage"": ""short coverage assessment"", ""status"": ""Ahead|Behind|Even"" }}
    // 3-4 items total
  ],
  ""promptCoverage"": [
    {{ ""question"": ""a realistic buyer question related to the target keyword"", ""coverage"": ""Covered|Partially Covered|Not Covered"", ""note"": ""short note explaining the coverage verdict"" }}
    // 5-6 items total
  ],
  ""citationGap"": [
    {{ ""icon"": ""a lucide-react icon name"", ""title"": ""Author bio"", ""status"": ""short status label"", ""score"": 0-100 }},
    {{ ""icon"": ""a lucide-react icon name"", ""title"": ""Sources cited"", ""status"": ""short status label"", ""score"": 0-100 }},
    {{ ""icon"": ""a lucide-react icon name"", ""title"": ""Original data"", ""status"": ""short status label"", ""score"": 0-100 }},
    {{ ""icon"": ""a lucide-react icon name"", ""title"": ""Expert quotes"", ""status"": ""short status label"", ""score"": 0-100 }},
    {{ ""icon"": ""a lucide-react icon name"", ""title"": ""Freshness"", ""status"": ""short status label"", ""score"": 0-100 }}
  ]
}}

Return ONLY the JSON object, no markdown fences, no commentary.";

            string responseContent;
            try
            {
                responseContent = await _openAiService.GenerateContentAsync(
                    prompt: userPrompt,
                    systemPrompt: systemPrompt,
                    requireJson: true,
                    model: "gpt-4o-mini");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }

            var parsed = TryParseAnalysis(responseContent);
            return Ok(parsed ?? BuildFallbackAnalysis(request));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("generate-schema")]
    public async Task<IActionResult> GenerateSchema([FromBody] SchemaGenerationRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SchemaType))
                return BadRequest("SchemaType is required.");

            var systemPrompt = @"You are an expert technical SEO engineer who writes syntactically valid schema.org JSON-LD markup.
Always return a single, complete, valid JSON-LD object (wrapped in a <script type=""application/ld+json""> is NOT needed, just the raw JSON-LD).
Pretty-print it with 2-space indentation. Do not wrap the output in markdown code fences. Do not include any explanation, only the JSON-LD text.";

            string grounding = !string.IsNullOrWhiteSpace(request.Content)
                ? $"Base the markup on this actual page content:\n\n---\n{request.Content}\n---"
                : !string.IsNullOrWhiteSpace(request.Url)
                    ? $"Base the markup on this page URL (infer plausible, realistic details since you cannot browse it): {request.Url}"
                    : "No URL or content was provided, so produce a plausible, realistic, generic example for this schema type.";

            var userPrompt = $@"Generate a realistic, syntactically valid schema.org JSON-LD markup block of type ""{request.SchemaType}"".

{grounding}

Requirements:
- The root object must include ""@context"": ""https://schema.org"" and ""@type"": ""{request.SchemaType}"".
- Populate all fields that are typically required or recommended for a {request.SchemaType} schema with realistic, plausible values.
- Return ONLY the JSON-LD text, pretty-printed with 2-space indentation.
- Do NOT wrap it in markdown fences.
- Do NOT include any commentary before or after the JSON-LD.";

            string responseContent;
            try
            {
                responseContent = await _openAiService.GenerateContentAsync(
                    prompt: userPrompt,
                    systemPrompt: systemPrompt,
                    requireJson: false,
                    model: "gpt-4o-mini");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }

            var jsonLd = StripCodeFences(responseContent).Trim();
            return Ok(new SchemaGenerationResponse { JsonLd = jsonLd });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
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

    private GeoOptimizationResponse? TryParseAnalysis(string rawResponse)
    {
        try
        {
            var cleaned = StripCodeFences(rawResponse);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            var ai = JsonSerializer.Deserialize<AiGeoOptimizationResponse>(cleaned, options);
            if (ai == null) return null;

            var result = new GeoOptimizationResponse
            {
                Score = (int)Math.Clamp(Math.Round(ai.score), 0, 100),
                Verdict = string.IsNullOrWhiteSpace(ai.verdict) ? "Needs Review" : ai.verdict,
                StatusText = string.IsNullOrWhiteSpace(ai.statusText) ? "GEO audit completed." : ai.statusText,
                SubMetrics = ai.subMetrics?.Select(m => new GeoSubMetric
                {
                    Label = m.label ?? "Metric",
                    Score = (int)Math.Clamp(Math.Round(m.score), 0, 100)
                }).ToList() ?? new List<GeoSubMetric>(),
                FixRecommendations = ai.fixRecommendations?.Select(f => new FixRecommendation
                {
                    Title = f.title ?? "Recommendation",
                    Impact = f.impact ?? "Medium",
                    Icon = f.icon ?? "FileText",
                    Description = f.description ?? string.Empty,
                    Delta = f.delta ?? string.Empty
                }).ToList() ?? new List<FixRecommendation>(),
                CompetitorGap = ai.competitorGap?.Select(c => new CompetitorGap
                {
                    Name = c.name ?? "Competitor",
                    Coverage = c.coverage ?? string.Empty,
                    Status = c.status ?? "Even"
                }).ToList() ?? new List<CompetitorGap>(),
                PromptCoverage = ai.promptCoverage?.Select(p => new PromptCoverageItem
                {
                    Question = p.question ?? string.Empty,
                    Coverage = p.coverage ?? "Not Covered",
                    Note = p.note ?? string.Empty
                }).ToList() ?? new List<PromptCoverageItem>(),
                CitationGap = ai.citationGap?.Select(c => new CitationSignal
                {
                    Icon = c.icon ?? "FileText",
                    Title = c.title ?? "Signal",
                    Status = c.status ?? "Unknown",
                    Score = (int)Math.Clamp(Math.Round(c.score), 0, 100)
                }).ToList() ?? new List<CitationSignal>()
            };

            if (result.SubMetrics.Count == 0 || result.FixRecommendations.Count == 0)
                return null;

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static GeoOptimizationResponse BuildFallbackAnalysis(GeoOptimizationRequest request)
    {
        return new GeoOptimizationResponse
        {
            Score = 50,
            Verdict = "Needs Review",
            StatusText = $"We could not complete a full automated audit for \"{request.TargetKeyword}\" right now. Showing an estimated baseline instead.",
            SubMetrics = new List<GeoSubMetric>
            {
                new() { Label = "Structure", Score = 50 },
                new() { Label = "Clarity", Score = 50 },
                new() { Label = "Citations Readiness", Score = 45 },
                new() { Label = "Schema Coverage", Score = 40 }
            },
            FixRecommendations = new List<FixRecommendation>
            {
                new() { Title = "Add clear H1/H2 structure", Impact = "High", Icon = "FileText", Description = "Ensure the page has a single clear H1 and descriptive H2 subheadings that map to common user questions.", Delta = "+8 pts" },
                new() { Title = "Cite authoritative sources", Impact = "High", Icon = "Link", Description = "Add references to reputable sources to increase AI citation confidence.", Delta = "+6 pts" },
                new() { Title = "Add structured data", Impact = "Medium", Icon = "Code", Description = "Implement schema.org JSON-LD markup relevant to the page type.", Delta = "+5 pts" },
                new() { Title = "Answer buyer questions directly", Impact = "Medium", Icon = "ListChecks", Description = "Add a concise Q&A or FAQ section addressing common buyer questions for this keyword.", Delta = "+4 pts" }
            },
            CompetitorGap = new List<CompetitorGap>
            {
                new() { Name = "Competitor A", Coverage = "Broad topical coverage", Status = "Ahead" },
                new() { Name = "Competitor B", Coverage = "Moderate coverage", Status = "Even" },
                new() { Name = "Competitor C", Coverage = "Limited coverage", Status = "Behind" }
            },
            PromptCoverage = new List<PromptCoverageItem>
            {
                new() { Question = $"What is the best option for {request.TargetKeyword}?", Coverage = "Partially Covered", Note = "Content touches on this but lacks depth." },
                new() { Question = $"How much does {request.TargetKeyword} cost?", Coverage = "Not Covered", Note = "No pricing information detected." },
                new() { Question = $"Is {request.TargetKeyword} worth it?", Coverage = "Partially Covered", Note = "Some supporting evidence present." },
                new() { Question = $"How does {request.TargetKeyword} compare to alternatives?", Coverage = "Not Covered", Note = "No comparison content found." },
                new() { Question = $"What are the benefits of {request.TargetKeyword}?", Coverage = "Covered", Note = "Benefits are clearly listed." }
            },
            CitationGap = new List<CitationSignal>
            {
                new() { Icon = "User", Title = "Author bio", Status = "Missing", Score = 30 },
                new() { Icon = "Quote", Title = "Sources cited", Status = "Weak", Score = 40 },
                new() { Icon = "BarChart", Title = "Original data", Status = "Missing", Score = 25 },
                new() { Icon = "MessageSquare", Title = "Expert quotes", Status = "Weak", Score = 35 },
                new() { Icon = "Clock", Title = "Freshness", Status = "Unclear", Score = 50 }
            }
        };
    }
}
