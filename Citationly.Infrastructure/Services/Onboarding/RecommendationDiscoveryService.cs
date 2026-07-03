using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Onboarding;

namespace Citationly.Infrastructure.Services.Onboarding;

public class RecommendationDiscoveryResponseWrapper
{
    public List<DiscoveryRecommendationDto>? recommendations { get; set; }
}

public class RecommendationDiscoveryService : IRecommendationDiscoveryService
{
    private readonly IOpenAiService _openAiService;

    public RecommendationDiscoveryService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<DiscoveryRecommendationDto>> DiscoverRecommendationsAsync(GapAnalysisResult gapAnalysis, string websiteUrl, string rawProfileJson)
    {
        var systemPrompt = "You are an expert in Generative Engine Optimization (GEO) and AI Search Optimization. Generate highly focused, actionable recommendations based on the provided gaps.";

        var gapSummary = gapAnalysis.GenerateSummaryString();

        var userPrompt = $@"Generate a focused GEO optimization roadmap for the business based on their specific performance gaps.

## Input

Website: {websiteUrl}
Profile: {rawProfileJson}

## Identified Performance Gaps:
{gapSummary}

## Objective
Generate between 30 and 60 recommendations that specifically address these gaps to maximize visibility in AI platforms (ChatGPT, Claude, Gemini, Perplexity, etc.).

Categories to consider:
- Technical SEO
- Content Improvements
- Schema Improvements
- Citation Opportunities
- Brand Authority Improvements
- Prompt Coverage Improvements
- Competitor Weaknesses

Return ONLY valid JSON in the exact schema below. Do NOT wrap in markdown blocks or include explanations.

{{
  ""recommendations"": [
    {{
      ""category"": ""(One of the categories above)"",
      ""title"": ""(Short, actionable title)"",
      ""description"": ""(1-2 sentence description of what needs to be done and why)""
    }}
  ]
}}";

        var responseContent = await _openAiService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini"); // Fast, cheap model since task is simplified

        responseContent = responseContent.Trim();
        if (responseContent.StartsWith("```json"))
        {
            responseContent = responseContent.Substring(7);
            if (responseContent.EndsWith("```"))
                responseContent = responseContent.Substring(0, responseContent.Length - 3);
        }
        if (responseContent.StartsWith("```"))
        {
            responseContent = responseContent.Substring(3);
            if (responseContent.EndsWith("```"))
                responseContent = responseContent.Substring(0, responseContent.Length - 3);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var parsed = JsonSerializer.Deserialize<RecommendationDiscoveryResponseWrapper>(responseContent, options);
        return parsed?.recommendations ?? new List<DiscoveryRecommendationDto>();
    }
}
