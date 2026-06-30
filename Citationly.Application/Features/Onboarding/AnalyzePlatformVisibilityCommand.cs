using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzePlatformVisibilityCommand : IRequest<PlatformVisibilityAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class PlatformVisibilityAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PlatformsAnalyzed { get; set; }
}

public class PlatformVisibilityResponse
{
    public PlatformVisibilitySummaryResponse? summary { get; set; }
    public List<PlatformVisibilityItemResponse>? platformScores { get; set; }
}

public class PlatformVisibilitySummaryResponse
{
    public double overallVisibilityScore { get; set; }
    public string? bestPlatform { get; set; }
    public string? weakestPlatform { get; set; }
    public double averageMentionRate { get; set; }
    public double averagePromptCoverage { get; set; }
}

public class PlatformVisibilityItemResponse
{
    public string? platform { get; set; }
    public double visibilityScore { get; set; }
    public string? averageRank { get; set; }
    public double mentionRate { get; set; }
    public double promptCoverage { get; set; }
    public double confidence { get; set; }
    public List<string>? strengths { get; set; }
    public List<string>? weaknesses { get; set; }
}

public class AnalyzePlatformVisibilityCommandHandler : IRequestHandler<AnalyzePlatformVisibilityCommand, PlatformVisibilityAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;

    public AnalyzePlatformVisibilityCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<PlatformVisibilityAnalysisResult> Handle(AnalyzePlatformVisibilityCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if already analyzed
        var existingSummary = await _websiteRepository.GetVisibilitySummaryAsync(request.OrganizationId);
        if (existingSummary != null)
        {
            return new PlatformVisibilityAnalysisResult
            {
                Success = true,
                PlatformsAnalyzed = 9
            };
        }

        // 1. Get required data
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
            return new PlatformVisibilityAnalysisResult { Success = false, Error = "Website profile not found." };

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        if (existingPrompts == null || !existingPrompts.Any())
            return new PlatformVisibilityAnalysisResult { Success = false, Error = "No AI search prompts found." };

        var promptsJson = JsonSerializer.Serialize(existingPrompts.Select(p => new {
            Query = p.QueryString,
            Topic = p.Topic
        }));

        var promptAnalysisJson = JsonSerializer.Serialize(existingPrompts.Select(p => new {
            Query = p.QueryString,
            VisibilityScore = p.VisibilityScore,
            EstimatedRank = p.EstimatedRank,
            AppearsInAnswer = p.AppearsInAnswer,
            BrandStrength = p.BrandStrength
        }));

        string systemPrompt = "You are an expert in Generative Engine Optimization (GEO), AI Search Optimization, SEO, Competitive Intelligence, and Large Language Model behavior.";

        string userPrompt = $@"Your task is to estimate the business's AI visibility across major AI search platforms.

## Input

Website
{profile.WebsiteUrl}

Website Profile
{profile.RawProfileJson}

Competitor Profile
(Extracted from context)

Generated Prompts
{promptsJson}

Prompt Analysis
{promptAnalysisJson}

## Objective

Estimate how visible the business is on each AI platform based on the Website Profile, Competitor Profile, Generated Prompts, and Prompt Analysis.

Analyze each platform independently.

Platforms
- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

Assume each platform uses different ranking signals.

Estimate visibility using:
- Brand Authority
- Content Authority
- Citation Strength
- Entity Recognition
- Topical Authority
- Website Quality
- GEO Readiness
- SEO Strength
- Trust Signals
- Competitor Strength

Return ONLY valid JSON.
Do NOT include markdown.
Do NOT include explanations.
Do NOT wrap inside ```json.

------------------------------------------------
Scoring Rules
Visibility Score
0–100
Meaning
0–20 Very Low
21–40 Low
41–60 Moderate
61–80 High
81–100 Excellent

Average Rank
Choose one
1–3
4–10
11–20
21–50
50+

Mention Rate
Estimate the percentage of prompts where the business would likely be mentioned.
0–100

Prompt Coverage
Estimate the percentage of generated prompts for which the business is likely to appear in AI-generated answers.
0–100

Confidence
0–100
Represents confidence in the platform prediction.

Strengths
Return an array of the platform-specific factors helping visibility.

Weaknesses
Return an array of platform-specific limitations.

------------------------------------------------
Platform Considerations

ChatGPT
Focus more on:
- Entity recognition
- Brand authority
- High-quality educational content
- Documentation
- Structured information

Claude
Focus more on:
- Long-form authoritative content
- Trustworthiness
- Clear technical explanations
- Research quality

Gemini
Focus more on:
- SEO
- Google indexing
- Structured data
- Knowledge Graph
- EEAT signals

Perplexity
Focus more on:
- Citations
- Fresh content
- References
- Authoritative sources

Google AI Overview
Focus more on:
- Traditional SEO
- EEAT
- Structured content
- Search authority
- Google visibility

Microsoft Copilot
Focus more on:
- Bing visibility
- Structured content
- Technical authority

Meta AI
Focus more on:
- Brand popularity
- Consumer awareness
- Public presence

DeepSeek
Focus more on:
- Technical content
- Open knowledge
- Developer resources

Grok
Focus more on:
- Current relevance
- Technical discussions
- Brand visibility
- Public conversations

------------------------------------------------
Return exactly this schema
{{
  ""summary"": {{
    ""overallVisibilityScore"": 0,
    ""bestPlatform"": """",
    ""weakestPlatform"": """",
    ""averageMentionRate"": 0,
    ""averagePromptCoverage"": 0
  }},
  ""platformScores"": [
    {{
      ""platform"": """",
      ""visibilityScore"": 0,
      ""averageRank"": """",
      ""mentionRate"": 0,
      ""promptCoverage"": 0,
      ""confidence"": 0,
      ""strengths"": [],
      ""weaknesses"": []
    }}
  ]
}}

Finally
Calculate
- overallVisibilityScore
- bestPlatform
- weakestPlatform
- averageMentionRate
- averagePromptCoverage

Return ONLY the JSON object.";

        try
        {
            var responseContent = await _openRouterService.GenerateContentAsync(
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
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };

            var result = JsonSerializer.Deserialize<PlatformVisibilityResponse>(responseContent, options);

            if (result != null && result.platformScores != null && result.summary != null)
            {
                var summaryId = Guid.NewGuid();
                var summary = new Citationly.Domain.Entities.VisibilitySummary
                {
                    Id = summaryId,
                    OrganizationId = request.OrganizationId,
                    OverallVisibilityScore = (int)Math.Round(result.summary.overallVisibilityScore),
                    BestPlatform = result.summary.bestPlatform ?? "",
                    WeakestPlatform = result.summary.weakestPlatform ?? "",
                    AverageMentionRate = (int)Math.Round(result.summary.averageMentionRate),
                    AveragePromptCoverage = (int)Math.Round(result.summary.averagePromptCoverage)
                };

                var visibilities = result.platformScores.Select(p => new PlatformVisibility
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = request.OrganizationId,
                    Platform = p.platform ?? "",
                    VisibilityScore = (int)Math.Round(p.visibilityScore),
                    AverageRank = p.averageRank ?? "",
                    MentionRate = (int)Math.Round(p.mentionRate),
                    PromptCoverage = (int)Math.Round(p.promptCoverage),
                    Confidence = (int)Math.Round(p.confidence),
                    StrengthsJson = p.strengths != null ? JsonSerializer.Serialize(p.strengths) : "[]",
                    WeaknessesJson = p.weaknesses != null ? JsonSerializer.Serialize(p.weaknesses) : "[]"
                }).ToList();

                await _websiteRepository.InsertPlatformVisibilityAsync(summary, visibilities);

                return new PlatformVisibilityAnalysisResult
                {
                    Success = true,
                    PlatformsAnalyzed = visibilities.Count
                };
            }
            else
            {
                return new PlatformVisibilityAnalysisResult { Success = false, Error = "Failed to parse AI response." };
            }
        }
        catch (Exception ex)
        {
            return new PlatformVisibilityAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
