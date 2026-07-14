using System;
using System.Text.Json;
using System.Threading.Tasks;
using Citationly.Application.Features.GeoOptimizer;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoOptimizer;

namespace Citationly.Infrastructure.Services.GeoOptimizer;

public class GeoOptimizerService : IGeoOptimizerService
{
    private readonly IOpenAiService _openAiService;
    private readonly IScraperEngine _scraperEngine;

    public GeoOptimizerService(IOpenAiService openAiService, IScraperEngine scraperEngine)
    {
        _openAiService = openAiService;
        _scraperEngine = scraperEngine;
    }

    public async Task<GeoOptimizationResponse> AnalyzeAsync(GeoOptimizationRequest request)
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

        // Truncate if too long, although gpt-4o-mini handles 128k context
        if (textToAnalyze.Length > 40000)
        {
            textToAnalyze = textToAnalyze.Substring(0, 40000);
        }

        var systemPrompt = $@"You are an expert in Generative Engine Optimization (GEO) and AI Search Optimization.
Your task is to analyze the provided content against the target keyword: '{request.TargetKeyword}' and the target AI engine(s): '{request.Engine}'.
Evaluate how likely AI engines are to cite this page. Provide exact fixes ranked by impact, competitor gap analysis, prompt coverage, and citation gap analysis.

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

        var userPrompt = $"Analyze the following content:\n\n{textToAnalyze}";

        var jsonResponse = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<GeoOptimizationResponse>(jsonResponse, options);
            return result ?? new GeoOptimizationResponse();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to parse AI response: {ex.Message}. Response: {jsonResponse}");
        }
    }

    public async Task<SchemaGenerationResponse> GenerateSchemaAsync(SchemaGenerationRequest request)
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

        var jsonResponse = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
        
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
}
