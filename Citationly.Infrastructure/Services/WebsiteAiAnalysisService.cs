using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class WebsiteAiAnalysisService : IAiAnalysisService
{
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IEmbeddingService _embeddingService;

    public WebsiteAiAnalysisService(IAiCompletionService aiCompletionService, IEmbeddingService embeddingService)
    {
        _aiCompletionService = aiCompletionService;
        _embeddingService = embeddingService;
    }

    public async Task<IEnumerable<Recommendation>> AnalyzePageAsync(CrawledPage page)
    {
        const string systemPrompt = "You are an expert in Generative Engine Optimization (GEO) and AI Search Optimization. Analyze real page content and produce specific, actionable recommendations.";

        var userPrompt = $@"Analyze this page and generate 1-3 specific, actionable recommendations to improve its
visibility in AI search answers (ChatGPT, Claude, Gemini, Perplexity).

Page URL: {page.Url}
Page title: {page.Title}
Page content:
{page.Content}

Base every recommendation on what's actually present or missing in the content above — do not invent
generic advice unrelated to this specific page.

Return ONLY valid JSON, no markdown:
{{
  ""recommendations"": [
    {{
      ""title"": ""(Short, actionable title)"",
      ""description"": ""(1-2 sentence description of what needs to change and why)"",
      ""actionType"": ""(e.g. Content Update, Schema Markup, Technical SEO)"",
      ""priority"": ""(High, Medium, or Low)""
    }}
  ]
}}";

        try
        {
            var completion = await _aiCompletionService.CompleteAsync(
                null,
                "website.page_analysis",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai");
            if (!completion.Success) return Enumerable.Empty<Recommendation>();

            var parsed = JsonSerializer.Deserialize<RecommendationResponse>(StripFences(completion.Content), JsonOpts);
            return parsed?.Recommendations?.Select(r => new Recommendation
            {
                Title = r.Title ?? "Untitled recommendation",
                Description = r.Description,
                ActionType = r.ActionType,
                Priority = r.Priority
            }) ?? Enumerable.Empty<Recommendation>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PageAnalysis] Failed for {page.Url}: {ex.Message}");
            return Enumerable.Empty<Recommendation>();
        }
    }

    public Task<double[]?> GenerateEmbeddingAsync(string text) => _embeddingService.GenerateEmbeddingAsync(text);

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json")) s = s[7..];
        if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private class RecommendationResponse
    {
        public List<RecommendationDto>? Recommendations { get; set; }
    }

    private class RecommendationDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ActionType { get; set; }
        public string? Priority { get; set; }
    }
}
