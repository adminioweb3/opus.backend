using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Citationly.Application.Features.GeoOptimizer;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoAudit;
using Citationly.Application.Interfaces.GeoOptimizer;

namespace Citationly.Infrastructure.Services.GeoOptimizer;

public class GeoOptimizerService : IGeoOptimizerService
{
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IScraperEngine _scraperEngine;
    private readonly IGeoTechnicalAuditService _geoTechnicalAuditService;

    public GeoOptimizerService(
        IAiCompletionService aiCompletionService,
        IScraperEngine scraperEngine,
        IGeoTechnicalAuditService geoTechnicalAuditService)
    {
        _aiCompletionService = aiCompletionService;
        _scraperEngine = scraperEngine;
        _geoTechnicalAuditService = geoTechnicalAuditService;
    }

    public async Task<GeoOptimizationResponse> AnalyzeAsync(Guid organizationId, GeoOptimizationRequest request)
    {
        var textToAnalyze = request.Content;
        GeoTechnicalAuditResult? technicalAudit = null;

        if (string.IsNullOrWhiteSpace(textToAnalyze) && !string.IsNullOrWhiteSpace(request.Url))
        {
            var scrapedPage = await _scraperEngine.ScrapeSinglePageAsync(request.Url, Guid.NewGuid());
            textToAnalyze = scrapedPage?.Content ?? scrapedPage?.MarkdownContent;
            technicalAudit = await _geoTechnicalAuditService.AuditAsync(request.Url);
        }
        else if (!string.IsNullOrWhiteSpace(request.Url))
        {
            technicalAudit = await _geoTechnicalAuditService.AuditAsync(request.Url);
        }

        if (string.IsNullOrWhiteSpace(textToAnalyze))
        {
            throw new Exception("No content provided and failed to extract content from the URL.");
        }

        // Truncate if too long, although gpt-4o-mini handles 128k context
        if (textToAnalyze.Length > 40000)
        {
            textToAnalyze = textToAnalyze.Substring(0, 40000);
        }

        var systemPrompt = $@"You are an expert in Generative Engine Optimization (GEO) and AI Search Optimization.
Your task is to analyze the provided content against the target keyword: '{request.TargetKeyword}' and the target AI engine(s): '{request.Engine}'.
Evaluate how likely AI engines are to cite this page. Provide exact fixes ranked by impact, competitor gap analysis, prompt coverage, and citation gap analysis.
When deterministic technical audit evidence is provided, treat it as ground truth. Do not invent robots.txt, sitemap, schema, SSR, heading, FAQ, or metadata findings that contradict the audit.

Return ONLY a valid JSON object matching the following structure:
{{
  ""Score"": <integer 0-100>,
  ""Verdict"": ""<Excellent | Needs work>"",
  ""StatusText"": ""<short status summary>"",
  ""SubMetrics"": [
    {{ ""Label"": ""Answer structure"", ""Score"": <integer 0-100, how quickly a direct answer appears near the top> }},
    {{ ""Label"": ""Prompt coverage"", ""Score"": <integer 0-100, how well the content answers likely buyer questions> }},
    {{ ""Label"": ""Citation authority"", ""Score"": <integer 0-100, strength of external sourcing/links/data> }},
    {{ ""Label"": ""Extractability"", ""Score"": <integer 0-100, how easily AI can lift standalone chunks (lists, short passages, subheads)> }},
    {{ ""Label"": ""Freshness signals"", ""Score"": <integer 0-100, presence of dates/recency signals> }}
  ],
  ""FixRecommendations"": [
    {{
      ""Title"": ""<short issue title>"",
      ""Impact"": ""<High | Medium | Low>"",
      ""Icon"": ""<one of: ti-quote, ti-link, ti-list-numbers, ti-code, ti-calendar, ti-alert-triangle, ti-file-text, ti-search, ti-clock, ti-tag>"",
      ""Description"": ""<1-2 sentences explaining the specific problem found in THIS content and the concrete fix>"",
      ""Delta"": ""<short green delta string e.g. '+18 GEO score', '+12 cite rate', '+9 extractability'>""
    }}
  ],
  ""CompetitorGap"": [
    {{ ""Name"": ""<competitor name>"", ""Coverage"": ""<percentage e.g. 85%>"", ""Status"": ""<Strong | Moderate | Weak>"" }}
  ],
  ""PromptCoverage"": [
    {{ ""Question"": ""<a realistic buyer question related to '{request.TargetKeyword}' that an AI engine gets asked>"", ""Coverage"": ""<Full | Partial | None>"", ""Note"": ""<1 sentence on exactly what is or isn't answered for this specific question>"" }}
  ],
  ""CitationGap"": [
    {{ ""Icon"": ""<one of: ti-chart-bar, ti-school, ti-quote, ti-link, ti-certificate>"", ""Title"": ""<authority signal name, e.g. 'Statistics with sources'>"", ""Status"": ""<short status string, e.g. '1 of ~7 expected'>"", ""Score"": <integer 0-100> }}
  ]
}}

Return exactly 5 SubMetrics with those exact labels, in that order. Return 3-6 FixRecommendations ordered by impact, each grounded in something specific and real from the provided content — not generic filler.
Return 5-6 PromptCoverage items covering distinct realistic buyer questions/search queries around the target keyword (not just the exact keyword itself), each judged against what the content actually covers.
Return exactly 5 CitationGap items, one each for: statistics with sources, expert/author attribution, original data or quotes, outbound authority links, and backlink/citation signals — scored based on what's actually present in the content.";

        var deterministicEvidence = technicalAudit == null
            ? "No deterministic URL audit was available; score the pasted content only."
            : JsonSerializer.Serialize(new
            {
                technicalAudit.Url,
                technicalAudit.OverallScore,
                technicalAudit.SeoHealthScore,
                technicalAudit.AeoReadinessScore,
                technicalAudit.PillarScores,
                technicalAudit.Checks,
                technicalAudit.EvidenceNotes
            });

        var userPrompt = $"Deterministic technical audit evidence:\n{deterministicEvidence}\n\nAnalyze the following content:\n\n{textToAnalyze}";

        var completion = await _aiCompletionService.CompleteAsync(
            organizationId,
            "geo_optimizer.analyze",
            userPrompt,
            systemPrompt,
            requireJson: true,
            preferredProviderKey: "openai");
        if (!completion.Success)
        {
            throw new Exception(completion.ErrorMessage ?? "GEO optimization analysis failed.");
        }

        var jsonResponse = completion.Content;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeoOptimizationResponse>(jsonResponse, options);
            result ??= new GeoOptimizationResponse();
            ApplyDeterministicAudit(result, technicalAudit);
            return result;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI response: {ex.Message}. Response: {jsonResponse}");
        }
    }

    public async Task<SchemaGenerationResponse> GenerateSchemaAsync(Guid organizationId, SchemaGenerationRequest request)
    {
        var textToAnalyze = request.Content;

        if (string.IsNullOrWhiteSpace(textToAnalyze) && !string.IsNullOrWhiteSpace(request.Url))
        {
            var scrapedPage = await _scraperEngine.ScrapeSinglePageAsync(request.Url, Guid.NewGuid());
            textToAnalyze = scrapedPage?.Content ?? scrapedPage?.MarkdownContent;
        }

        if (string.IsNullOrWhiteSpace(textToAnalyze))
        {
            throw new Exception("No content provided and failed to extract content from the URL.");
        }

        if (textToAnalyze.Length > 20000)
        {
            textToAnalyze = textToAnalyze.Substring(0, 20000);
        }

        var systemPrompt = $@"You are an expert in structured data and schema.org JSON-LD generation.
Your task is to generate valid JSON-LD for the schema type '{request.SchemaType}' based on the provided content.
Make sure to extract relevant information (like FAQs, authors, product details, etc.) from the content to populate the schema.
Return ONLY valid JSON. Do not wrap it in markdown code blocks like ```json ... ```. Just return the raw JSON object.";

        var userPrompt = $"Generate {request.SchemaType} schema for the following content:\n\n{textToAnalyze}";

        var completion = await _aiCompletionService.CompleteAsync(
            organizationId,
            "geo_optimizer.generate_schema",
            userPrompt,
            systemPrompt,
            requireJson: true,
            preferredProviderKey: "openai");
        if (!completion.Success)
        {
            throw new Exception(completion.ErrorMessage ?? "Schema generation failed.");
        }

        var jsonResponse = completion.Content;
        
        // Strip markdown backticks if OpenAI still includes them
        if (jsonResponse.StartsWith("```json"))
        {
            jsonResponse = jsonResponse.Substring(7);
            if (jsonResponse.EndsWith("```"))
                jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 3);
        }

        return new SchemaGenerationResponse
        {
            JsonLd = jsonResponse.Trim()
        };
    }

    private static void ApplyDeterministicAudit(GeoOptimizationResponse response, GeoTechnicalAuditResult? audit)
    {
        if (audit == null)
        {
            response.ScoreSource = string.IsNullOrWhiteSpace(response.ScoreSource)
                ? "llm_content_analysis"
                : response.ScoreSource;
            response.DeterministicScore = response.Score;
            return;
        }

        response.Score = audit.OverallScore;
        response.DeterministicScore = audit.OverallScore;
        response.ScoreSource = "deterministic_technical_audit_v1";
        response.Verdict = audit.OverallScore >= 75 ? "Excellent" : "Needs work";
        response.StatusText = $"Deterministic GEO audit: SEO {audit.SeoHealthScore}, AEO {audit.AeoReadinessScore}";
        response.TechnicalChecks = audit.Checks
            .Select(check => new GeoTechnicalCheckDto
            {
                Key = check.Key,
                Label = check.Label,
                Score = check.Score,
                Passed = check.Passed,
                Evidence = check.Evidence
            })
            .ToList();
        response.EvidenceNotes = audit.EvidenceNotes.ToList();

        response.SubMetrics = new List<GeoSubMetric>
        {
            new() { Label = "Answer structure", Score = audit.PillarScores.GetValueOrDefault("answerReadiness") },
            new() { Label = "Prompt coverage", Score = Average(audit.PillarScores.GetValueOrDefault("entityClarity"), audit.PillarScores.GetValueOrDefault("freshness")) },
            new() { Label = "Citation authority", Score = audit.PillarScores.GetValueOrDefault("authoritySignals") },
            new() { Label = "Extractability", Score = audit.PillarScores.GetValueOrDefault("extractability") },
            new() { Label = "Freshness signals", Score = audit.PillarScores.GetValueOrDefault("freshness") }
        };

        var deterministicFixes = audit.Checks
            .Where(check => check.Score < 70)
            .OrderBy(check => check.Score)
            .Take(4)
            .Select(ToRecommendation)
            .ToList();

        response.FixRecommendations = deterministicFixes
            .Concat(response.FixRecommendations.Where(aiFix =>
                !deterministicFixes.Any(detFix => string.Equals(detFix.Title, aiFix.Title, StringComparison.OrdinalIgnoreCase))))
            .Take(6)
            .ToList();
    }

    private static int Average(params int[] scores)
    {
        return scores.Length == 0 ? 0 : (int)Math.Round(scores.Average());
    }

    private static GeoFixRecommendation ToRecommendation(GeoTechnicalCheck check)
    {
        var (title, icon, description) = check.Key switch
        {
            "robots_ai_access" => (
                "Allow AI crawler access where appropriate",
                "ti-code",
                $"{check.Evidence}. Review robots.txt so approved AI crawlers can reach citation-worthy public pages."),
            "sitemap" => (
                "Publish a valid XML sitemap",
                "ti-link",
                $"{check.Evidence}. Add or repair sitemap.xml so AI/search crawlers can discover canonical content."),
            "structured_data" => (
                "Add page-level structured data",
                "ti-code",
                $"{check.Evidence}. Add Article, Product, FAQPage, or Organization JSON-LD that matches the page intent."),
            "faq_schema" => (
                "Add extractable FAQ answers",
                "ti-quote",
                $"{check.Evidence}. Include concise question/answer blocks and FAQPage schema for buyer questions."),
            "heading_structure" => (
                "Fix heading hierarchy",
                "ti-list-numbers",
                $"{check.Evidence}. Use one descriptive H1 and clear H2 sections that map to user questions."),
            "metadata" => (
                "Improve title and meta description",
                "ti-tag",
                $"{check.Evidence}. Write concise metadata that names the entity, topic, and page promise."),
            "ssr_content" => (
                "Expose crawlable server-rendered content",
                "ti-file-text",
                $"{check.Evidence}. Ensure the initial HTML contains meaningful body copy, not only a JavaScript shell."),
            "freshness" => (
                "Add visible freshness signals",
                "ti-calendar",
                $"{check.Evidence}. Add published/updated dates for topics where recency affects AI trust."),
            "authority_signals" => (
                "Cite authoritative supporting sources",
                "ti-link",
                $"{check.Evidence}. Add reputable outbound citations, source notes, or proof points AI systems can quote."),
            _ => (
                $"Improve {check.Label}",
                "ti-alert-triangle",
                $"{check.Evidence}. Address this deterministic audit gap before relying on AI-written recommendations.")
        };

        return new GeoFixRecommendation
        {
            Title = title,
            Impact = check.Score < 40 ? "High" : "Medium",
            Icon = icon,
            Description = description,
            Delta = $"+{Math.Max(4, (100 - check.Score) / 5)} GEO score"
        };
    }
}
