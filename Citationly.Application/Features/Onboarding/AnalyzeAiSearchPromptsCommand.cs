using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text.Json.Serialization;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeAiSearchPromptsCommand : IRequest<AiSearchPromptsAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class AiSearchPromptsAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalPrompts { get; set; }
}

public class AiPromptResponse
{
    public AiPromptSummary? summary { get; set; }
    public List<AiPromptTopicGroup>? topics { get; set; }
}

public class AiPromptSummary
{
    public double totalPrompts { get; set; }
    public List<string>? topicsCovered { get; set; }
    public List<string>? personasCovered { get; set; }
    public List<string>? regionsCovered { get; set; }
    public List<string>? languagesCovered { get; set; }
}

public class AiPromptTopicGroup
{
    public string? topic { get; set; }
    public List<AiPromptItem>? prompts { get; set; }
}

public class AiPromptItem
{
    public string? promptId { get; set; }
    public string? prompt { get; set; }
    public string? topic { get; set; }
    public string? intent { get; set; }
    public string? difficulty { get; set; }
    public string? monthlySearchEstimate { get; set; }
    public string? persona { get; set; }
    public string? region { get; set; }
    public string? language { get; set; }
    public double commercialValue { get; set; }
}

public class AnalyzeAiSearchPromptsCommandHandler : IRequestHandler<AnalyzeAiSearchPromptsCommand, AiSearchPromptsAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IOpenRouterService _openRouterService;

    public AnalyzeAiSearchPromptsCommandHandler(
        IWebsiteRepository websiteRepository,
        IOpenRouterService openRouterService)
    {
        _websiteRepository = websiteRepository;
        _openRouterService = openRouterService;
    }

    public async Task<AiSearchPromptsAnalysisResult> Handle(AnalyzeAiSearchPromptsCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if prompts already exist for this organization
        int existingCount = await _websiteRepository.GetAiSearchPromptCountAsync(request.OrganizationId);
        if (existingCount > 0)
        {
            return new AiSearchPromptsAnalysisResult
            {
                Success = true,
                TotalPrompts = existingCount
            };
        }

        // 1. Get the latest Website Profile
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new AiSearchPromptsAnalysisResult { Success = false, Error = "Website profile not found for this organization." };
        }

        string websiteProfile = profile.RawProfileJson;

        var systemPrompt = "You are an expert AI Search, Generative Engine Optimization (GEO), SEO, Content Strategy, and Buyer Intent Analyst.";
        
        var userPrompt = $@"Your task is to generate realistic prompts that potential customers would ask AI search engines when searching for products or services similar to the provided business.

## Input

Website Profile
{websiteProfile}

Competitor Profile
(Extracted from industry context)

## Objective

Generate between **50 and 100** unique, realistic prompts that prospective customers would naturally ask AI assistants such as:
- ChatGPT
- Claude
- Gemini
- Perplexity
- Google AI Overview
- Microsoft Copilot
- Meta AI
- DeepSeek
- Grok

The prompts should closely match real conversational behavior rather than traditional keyword searches.

## Instructions

1. Use the Website Profile to understand:
   - Services
   - Products
   - Industry
   - Business Model
   - Target Audience
   - Market Position
   - Customer Pain Points
   - Buying Intent

2. Generate prompts covering every stage of the customer journey: Awareness, Consideration, Evaluation, Decision, Purchase, Comparison, Implementation, Support.

3. Group prompts into relevant topics.

4. Prompt Quality Rules:
Prompts should sound exactly like questions people ask AI assistants. Avoid keyword stuffing. Avoid repetitive wording. 
Generate a mix of: Questions, Comparisons, Recommendations, Best practices, Pricing, Vendor selection, Implementation, Case studies, Technical guidance, Business advice.

5. Intent must be one of: Informational, Commercial, Transactional, Navigational, Comparative, Educational.

6. Difficulty: Easy, Medium, Hard.

7. Monthly Search Estimate: Very Low, Low, Medium, High, Very High.

8. Persona examples: Startup Founder, CTO, CEO, Product Manager, Engineering Manager, IT Director, Enterprise Architect, Marketing Manager, Operations Manager, Business Owner, Procurement Manager, Developer.

9. Region examples: Global, North America, Europe, India, Middle East, APAC.

10. Language: English unless the business profile clearly indicates another language.

11. Commercial Value: Score between 1 and 10 (1 = Very Low Buying Intent, 10 = Extremely High Buying Intent).

12. Ensure:
- Every prompt is unique. No duplicates.
- No keyword variations with only one word changed.
- Prompts must feel human.
- Cover all identified services, industries served, personas, and buyer intents.

13. Assign a unique Prompt ID (e.g. PROMPT-001).

14. CRITICAL: You MUST return a minimum of 50 prompts. Do NOT stop early. Return ONLY valid JSON. Do NOT include markdown. Do NOT wrap in ```json.

Return exactly this schema:
{{
  ""summary"": {{
    ""totalPrompts"": 0,
    ""topicsCovered"": [],
    ""personasCovered"": [],
    ""regionsCovered"": [],
    ""languagesCovered"": []
  }},
  ""topics"": [
    {{
      ""topic"": """",
      ""prompts"": [
        {{
          ""promptId"": ""PROMPT-001"",
          ""prompt"": """",
          ""topic"": """",
          ""intent"": """",
          ""difficulty"": """",
          ""monthlySearchEstimate"": """",
          ""persona"": """",
          ""region"": """",
          ""language"": """",
          ""commercialValue"": 0
        }}
      ]
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
                model: "meta-llama/llama-3.3-70b-instruct:free");

            // Clean up markdown just in case
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

            var result = JsonSerializer.Deserialize<AiPromptResponse>(responseContent, options);

            if (result != null && result.topics != null && result.topics.Any())
            {
                var entities = new List<AiSearchPrompt>();

                foreach (var topicGroup in result.topics)
                {
                    if (topicGroup.prompts == null) continue;

                    foreach (var p in topicGroup.prompts)
                    {
                        var rawJson = JsonSerializer.Serialize(p, options);

                        entities.Add(new AiSearchPrompt
                        {
                            OrganizationId = request.OrganizationId,
                            QueryString = p.prompt ?? "",
                            SearchEngine = "Google", // Default
                            Topic = p.topic ?? topicGroup.topic,
                            Intent = p.intent,
                            Difficulty = p.difficulty,
                            Persona = p.persona,
                            CommercialValue = (int)Math.Round(p.commercialValue),
                            RawJson = rawJson,
                            GeneratedAt = DateTime.UtcNow
                        });
                    }
                }

                if (entities.Any())
                {
                    await _websiteRepository.InsertAiSearchPromptsAsync(entities);
                }

                return new AiSearchPromptsAnalysisResult
                {
                    Success = true,
                    TotalPrompts = entities.Count
                };
            }
            else
            {
                return new AiSearchPromptsAnalysisResult { Success = false, Error = "Failed to parse AI response or no prompts generated." };
            }
        }
        catch (Exception ex)
        {
            return new AiSearchPromptsAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
