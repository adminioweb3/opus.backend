using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Visibility;

public class PlatformInsightService : IPlatformInsightService
{
    private readonly IOpenAiService _openAiService;
    private readonly IWebsiteRepository _websiteRepository;

    public PlatformInsightService(IOpenAiService openAiService, IWebsiteRepository websiteRepository)
    {
        _openAiService = openAiService;
        _websiteRepository = websiteRepository;
    }

    public async Task GenerateInsightAsync(PlatformVisibility platform, WebsiteProfile profile, List<AiSearchPrompt> prompts)
    {
        if (platform == null) return;

        var systemPrompt = "You are an expert in Generative Engine Optimization (GEO) and AI Search Optimization.";

        // Summarize prompts for token optimization (Don't send all 100 raw prompts)
        int totalPrompts = prompts.Count;
        int inAnswerCount = prompts.Count(p => p.AppearsInAnswer);
        double avgBrand = prompts.Any() ? prompts.Average(p => p.BrandStrength) : 0;
        double avgContent = prompts.Any() ? prompts.Average(p => p.ContentStrength) : 0;
        double avgCitation = prompts.Any() ? prompts.Average(p => p.CitationStrength) : 0;

        var promptSummary = $@"Total Prompts Analyzed: {totalPrompts}
Appears in Answer: {inAnswerCount}
Avg Brand Strength: {avgBrand:F1}/100
Avg Content Strength: {avgContent:F1}/100
Avg Citation Strength: {avgCitation:F1}/100";

        var userPrompt = $@"
Analyze qualitative insights for the following business specifically on the AI platform: {platform.Platform}.

## Input
Website Profile:
{profile.RawProfileJson}

Prompt Summary Data:
{promptSummary}

Platform Base Score: {platform.VisibilityScore}/100

## Objective
Generate qualitative insights (strengths, weaknesses, and a short explanation) for how this business appears on {platform.Platform}.
- Strengths (max 3 bullet points)
- Weaknesses (max 3 bullet points)
- Explanation (max 30 words)

## Rules
- Focus ONLY on {platform.Platform}'s specific ranking signals (e.g. ChatGPT focuses on entity recognition/brand, Perplexity on citations/references, etc).
- Return ONLY valid JSON.
- Do NOT wrap inside ```json.

Return exactly this schema:
{{
  ""strengths"": [""strength 1""],
  ""weaknesses"": [""weakness 1""],
  ""explanation"": ""Short explanation here.""
}}
";

        var responseContent = await _openAiService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

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
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var result = JsonSerializer.Deserialize<InsightResponse>(responseContent, options);

        if (result != null)
        {
            platform.StrengthsJson = JsonSerializer.Serialize(result.strengths ?? new List<string>());
            platform.WeaknessesJson = JsonSerializer.Serialize(result.weaknesses ?? new List<string>());
            platform.Explanation = result.explanation ?? "";
            platform.IsEnriched = true;
            
            await _websiteRepository.UpdatePlatformVisibilityAsync(platform);
        }
    }

    private class InsightResponse
    {
        public List<string>? strengths { get; set; }
        public List<string>? weaknesses { get; set; }
        public string? explanation { get; set; }
    }
}
