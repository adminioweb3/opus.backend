using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text.Json.Serialization;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeVisibilityCommand : IRequest<VisibilityAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class VisibilityAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalPromptsAnalyzed { get; set; }
}

public class VisibilityResponse
{
    public VisibilitySummary? summary { get; set; }
    public List<VisibilityAnalysisItem>? analysis { get; set; }
}

public class VisibilitySummary
{
    public double totalPrompts { get; set; }
    public double averageVisibilityScore { get; set; }
    public double averageMentionProbability { get; set; }
    public double averageShareOfVoice { get; set; }
    public double highVisibilityPrompts { get; set; }
    public double mediumVisibilityPrompts { get; set; }
    public double lowVisibilityPrompts { get; set; }
}

public class VisibilityAnalysisItem
{
    public string? promptId { get; set; }
    public string? prompt { get; set; }
    public string? topic { get; set; }
    public double visibilityScore { get; set; }
    public string? estimatedRank { get; set; }
    public double confidence { get; set; }
    public bool appearsInAnswer { get; set; }
    public double shareOfVoiceContribution { get; set; }
    public double mentionProbability { get; set; }
    public double brandStrength { get; set; }
    public double contentStrength { get; set; }
    public double citationStrength { get; set; }
    public string? reason { get; set; }
}

public class AnalyzeVisibilityCommandHandler : IRequestHandler<AnalyzeVisibilityCommand, VisibilityAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenAiService _openRouterService;

    public AnalyzeVisibilityCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenAiService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<VisibilityAnalysisResult> Handle(AnalyzeVisibilityCommand request, CancellationToken cancellationToken)
    {
        // 1. Get Prompts
        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        if (existingPrompts == null || !existingPrompts.Any())
        {
            return new VisibilityAnalysisResult { Success = false, Error = "No AI Search Prompts found for this organization. Generate them first." };
        }

        // 0. Check if visibility data is already populated
        if (existingPrompts.Any(p => !string.IsNullOrEmpty(p.VisibilityReason)))
        {
            return new VisibilityAnalysisResult
            {
                Success = true,
                TotalPromptsAnalyzed = existingPrompts.Count()
            };
        }

        // 2. Get the latest Website Profile
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new VisibilityAnalysisResult { Success = false, Error = "Website profile not found for this organization." };
        }

        string websiteUrl = profile.WebsiteUrl;
        string websiteProfile = profile.RawProfileJson;

        var promptsListForAi = existingPrompts.Select(p => new {
            Id = p.Id, 
            Prompt = p.QueryString,
            Topic = p.Topic
        }).ToList();
        
        string generatedPromptsJson = JsonSerializer.Serialize(promptsListForAi);

        var systemPrompt = "You are an expert Generative Engine Optimization (GEO), AI Search Visibility, SEO, Content Strategy, and Competitive Intelligence Analyst.";
        
        var userPrompt = $@"Your task is to estimate how likely the provided business is to appear in AI-generated answers for each generated prompt.

## Input

Website
{websiteUrl}

Website Profile
{websiteProfile}

Competitor Profile
(Extracted from industry context)

Generated Prompts
{generatedPromptsJson}

## Objective

For every generated prompt, estimate the business's AI visibility across modern AI search platforms including:
- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

Assume the AI models answer using a combination of:
- Public knowledge
- Website content
- Brand authority
- Topical authority
- Citations
- Industry reputation
- Content quality
- Competitor strength
- Semantic relevance

Estimate how visible the business would be compared with competitors.

## Instructions

1. Analyze each prompt independently.
2. Compare the business against identified competitors.
3. Never invent factual rankings.
4. Scores are predictive estimates.
5. Return ONLY valid JSON.
6. Do NOT include markdown.
7. Do NOT include explanations.
8. Do NOT wrap inside ```json.
9. Every prompt must contain exactly one analysis object.
10. Return all prompts.

----------------------------------------------------
Scoring Rules
----------------------------------------------------
Visibility Score
0-100
Meaning
0-20 = Almost impossible to appear
21-40 = Low visibility
41-60 = Moderate visibility
61-80 = Strong visibility
81-100 = Very likely to appear

Estimated Rank
Choose one
1-3
4-10
11-20
21+
Not Likely

Confidence
0-100
Confidence reflects confidence in your prediction.

Appears In Answer
true
false

Share Of Voice Contribution
0-100
Represents how much of the final AI answer this business would likely occupy compared to competitors.

Mention Probability
0-100
Probability the company name would be mentioned.

Brand Strength
0-100

Content Strength
0-100

Citation Strength
0-100

Reason
Provide a concise explanation in 1–2 sentences describing why the estimated visibility was assigned.

Return exactly this JSON schema
{{
  ""summary"": {{
    ""totalPrompts"": 0,
    ""averageVisibilityScore"": 0,
    ""averageMentionProbability"": 0,
    ""averageShareOfVoice"": 0,
    ""highVisibilityPrompts"": 0,
    ""mediumVisibilityPrompts"": 0,
    ""lowVisibilityPrompts"": 0
  }},
  ""analysis"": [
    {{
      ""promptId"": """", // IMPORTANT: Must exactly match the 'Id' field provided in Generated Prompts
      ""prompt"": """",
      ""topic"": """",
      ""visibilityScore"": 0,
      ""estimatedRank"": """",
      ""confidence"": 0,
      ""appearsInAnswer"": false,
      ""shareOfVoiceContribution"": 0,
      ""mentionProbability"": 0,
      ""brandStrength"": 0,
      ""contentStrength"": 0,
      ""citationStrength"": 0,
      ""reason"": """"
    }}
  ]
}}

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

            var result = JsonSerializer.Deserialize<VisibilityResponse>(responseContent, options);

            if (result != null && result.analysis != null && result.analysis.Any())
            {
                var promptMap = existingPrompts.ToDictionary(p => p.Id.ToString(), p => p, StringComparer.OrdinalIgnoreCase);
                var promptsToUpdate = new List<AiSearchPrompt>();

                foreach (var item in result.analysis)
                {
                    if (item.promptId != null && promptMap.TryGetValue(item.promptId, out var dbPrompt))
                    {
                        dbPrompt.VisibilityScore = (int)Math.Round(item.visibilityScore);
                        dbPrompt.EstimatedRank = item.estimatedRank;
                        dbPrompt.Confidence = (int)Math.Round(item.confidence);
                        dbPrompt.AppearsInAnswer = item.appearsInAnswer;
                        dbPrompt.ShareOfVoiceContribution = (int)Math.Round(item.shareOfVoiceContribution);
                        dbPrompt.MentionProbability = (int)Math.Round(item.mentionProbability);
                        dbPrompt.BrandStrength = (int)Math.Round(item.brandStrength);
                        dbPrompt.ContentStrength = (int)Math.Round(item.contentStrength);
                        dbPrompt.CitationStrength = (int)Math.Round(item.citationStrength);
                        dbPrompt.VisibilityReason = item.reason;
                        
                        promptsToUpdate.Add(dbPrompt);
                    }
                }

                if (promptsToUpdate.Any())
                {
                    await _websiteRepository.UpdateAiSearchPromptsVisibilityAsync(promptsToUpdate);
                }

                return new VisibilityAnalysisResult
                {
                    Success = true,
                    TotalPromptsAnalyzed = promptsToUpdate.Count
                };
            }
            else
            {
                return new VisibilityAnalysisResult { Success = false, Error = "Failed to parse AI response or no analysis generated." };
            }
        }
        catch (Exception ex)
        {
            return new VisibilityAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
