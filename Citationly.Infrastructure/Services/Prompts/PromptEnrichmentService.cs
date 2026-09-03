using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptEnrichmentService : IPromptEnrichmentService
{
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IWebsiteRepository _websiteRepository;

    public PromptEnrichmentService(IAiCompletionService aiCompletionService, IWebsiteRepository websiteRepository)
    {
        _aiCompletionService = aiCompletionService;
        _websiteRepository = websiteRepository;
    }

    public async Task EnrichPromptsBatchAsync(List<AiSearchPrompt> batch)
    {
        if (batch == null || !batch.Any()) return;

        var organizationId = batch.FirstOrDefault()?.OrganizationId;
        var profile = organizationId.HasValue
            ? await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId.Value)
            : null;
        var businessName = profile?.BusinessName ?? string.Empty;

        var systemPrompt = "You are an expert AI Search Prompt Enriched Metadata Analyzer.";

        // Prepare batch json for prompt
        var batchInput = batch.Select(p => new { Id = p.Id, Prompt = p.QueryString, Topic = p.Topic }).ToList();
        var batchJson = JsonSerializer.Serialize(batchInput);

        var userPrompt = $@"
You are classifying prompts used in an AI brand visibility / GEO measurement system.

The classification determines which metrics each prompt is allowed to affect.

Target brand:
{businessName}

## Input Batch
{batchJson}

## Prompt Classes
- Discovery: The user wants AI to identify companies, vendors, agencies, providers, platforms or specialists.
- Recommendation: The user explicitly asks which provider/company they should consider, shortlist or hire.
- ProviderEvaluation: The user asks how to evaluate providers, pricing, requirements, selection criteria, capabilities or purchasing decisions without necessarily asking AI to name companies.
- Informational: The user wants educational or explanatory information and does not need provider/company recommendations.
- BrandedComparison: The prompt explicitly mentions the target business and compares it with another provider.
- BrandedResearch: The prompt explicitly mentions the target business but is not necessarily a direct comparison.
- Navigational: The user is specifically trying to find a known company, website, product or page.

## Metric Rules
- OrganicVisibility: Use only for Discovery or Recommendation prompts where AI would reasonably be expected to return named providers.
- AnswerReadiness: Use for ProviderEvaluation and Informational prompts.
- BrandedPresence: Use for BrandedComparison, BrandedResearch or Navigational prompts.
- Excluded: Use when the prompt is malformed, irrelevant or cannot produce a meaningful measurement.

## Critical Rules
1. A prompt that explicitly contains the target brand must not affect OrganicVisibility.
2. A question like ""What factors should I consider when choosing a provider?"" must not count as an OrganicVisibility failure merely because the AI does not mention a company.
3. IsOrganicVisibilityEligible can only be true when the user's natural expectation is that AI may return named companies.
4. ExpectsBrandMention means a brand mention is a reasonable expectation based on the question itself.
5. VisibilityWeight is 1.0 for Discovery and Recommendation. Use 0.0 for every other prompt class.

## Additional Enrichment
For each prompt, also generate the existing metadata:
- Intent: (Informational, Commercial, Transactional, Navigational, Comparative, Educational)
- Persona: (Startup Founder, CTO, CEO, Product Manager, etc.)
- Difficulty: (Easy, Medium, Hard)
- EstimatedInterestLevel: your own qualitative guess at how much search interest this prompt
  might attract, based only on the prompt text itself - NOT a real measurement. One of:
  (Very Low, Low, Medium, High, Very High)
- Region: (Global, North America, Europe, etc.)
- Language: (English or specific language based on text)
- CommercialValue: Score 1-10
- TopicValidation: Validate and refine the topic categorisation.
- BuyerJourneyStage: (Awareness, Research, Problem Discovery, Evaluation, Comparison, Decision, Purchase, Implementation, Support)

## Instructions
1. Output MUST be an array of enriched items corresponding to the Input Batch.
2. Provide exactly the same 'Id' for matching.
3. Return ONLY valid JSON. Do NOT include markdown. Do NOT wrap in ```json.

Return exactly this schema:
{{
  ""enrichedPrompts"": [
    {{
      ""id"": ""uuid"",
      ""promptClass"": ""Discovery"",
      ""isBranded"": false,
      ""isOrganicVisibilityEligible"": true,
      ""expectsProviderRecommendations"": true,
      ""expectsBrandMention"": true,
      ""metricBucket"": ""OrganicVisibility"",
      ""visibilityWeight"": 1.0,
      ""classificationConfidence"": 0.95,
      ""scoringReason"": ""User explicitly asks AI to identify service providers."",
      ""intent"": """",
      ""persona"": """",
      ""difficulty"": """",
      ""estimatedInterestLevel"": """",
      ""region"": """",
      ""language"": """",
      ""commercialValue"": 0,
      ""topicValidation"": """",
      ""buyerJourneyStage"": """"
    }}
  ]
}}
";

        var completion = await _aiCompletionService.CompleteAsync(
            organizationId,
            "prompts.enrichment",
            userPrompt,
            systemPrompt,
            requireJson: true,
            preferredProviderKey: "openai");
        if (!completion.Success) return;

        // Clean up markdown just in case
        var responseContent = completion.Content.Trim();
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

        var result = JsonSerializer.Deserialize<EnrichmentResponse>(responseContent, options);

        if (result != null && result.enrichedPrompts != null)
        {
            foreach (var enriched in result.enrichedPrompts)
            {
                if (Guid.TryParse(enriched.id, out var promptId))
                {
                    var promptEntity = batch.FirstOrDefault(p => p.Id == promptId);
                    if (promptEntity != null)
                    {
                        promptEntity.Intent = enriched.intent ?? promptEntity.Intent;
                        promptEntity.Persona = enriched.persona ?? promptEntity.Persona;
                        promptEntity.Difficulty = enriched.difficulty ?? promptEntity.Difficulty;
                        promptEntity.EstimatedInterestLevel = enriched.estimatedInterestLevel ?? promptEntity.EstimatedInterestLevel;
                        promptEntity.Region = enriched.region ?? promptEntity.Region;
                        promptEntity.Language = enriched.language ?? promptEntity.Language;
                        promptEntity.CommercialValue = enriched.commercialValue;
                        promptEntity.TopicValidation = enriched.topicValidation ?? promptEntity.TopicValidation;
                        promptEntity.BuyerJourneyStage = enriched.buyerJourneyStage ?? promptEntity.BuyerJourneyStage;
                        promptEntity.PromptClass = NormalizePromptClass(enriched.promptClass) ?? promptEntity.PromptClass;
                        promptEntity.IsBranded = enriched.isBranded;
                        promptEntity.IsOrganicVisibilityEligible = enriched.isOrganicVisibilityEligible;
                        promptEntity.ExpectsProviderRecommendations = enriched.expectsProviderRecommendations;
                        promptEntity.ExpectsBrandMention = enriched.expectsBrandMention;
                        promptEntity.MetricBucket = NormalizeMetricBucket(enriched.metricBucket) ?? promptEntity.MetricBucket;
                        promptEntity.VisibilityWeight = Clamp01(enriched.visibilityWeight);
                        promptEntity.ClassificationConfidence = Clamp01(enriched.classificationConfidence);
                        promptEntity.ScoringReason = enriched.scoringReason ?? promptEntity.ScoringReason;

                        ApplyDeterministicClassification(promptEntity, businessName);

                        promptEntity.IsEnriched = true;
                        promptEntity.EnrichedAt = DateTime.UtcNow;
                        
                        promptEntity.RawJson = JsonSerializer.Serialize(enriched, options);
                    }
                }
            }
        }

        // Save batch to database
        await _websiteRepository.UpdateAiSearchPromptsAsync(batch);
    }

    private static void ApplyDeterministicClassification(AiSearchPrompt promptEntity, string businessName)
    {
        if (ContainsBrand(promptEntity.QueryString, businessName))
        {
            promptEntity.IsBranded = true;
            promptEntity.IsOrganicVisibilityEligible = false;
            promptEntity.VisibilityWeight = 0;
            promptEntity.ExpectsBrandMention = true;
            promptEntity.MetricBucket = "BrandedPresence";

            if (promptEntity.PromptClass is not "BrandedComparison" and not "BrandedResearch" and not "Navigational")
            {
                promptEntity.PromptClass = LooksComparative(promptEntity.QueryString)
                    ? "BrandedComparison"
                    : "BrandedResearch";
            }

            promptEntity.ScoringReason ??= "Prompt explicitly contains the target brand, so it belongs in branded presence instead of organic visibility.";
            return;
        }

        if (string.Equals(promptEntity.PromptClass, "Discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(promptEntity.PromptClass, "Recommendation", StringComparison.OrdinalIgnoreCase))
        {
            promptEntity.MetricBucket = "OrganicVisibility";
            promptEntity.IsOrganicVisibilityEligible = true;
            promptEntity.VisibilityWeight = promptEntity.VisibilityWeight <= 0 ? 1 : promptEntity.VisibilityWeight;
            promptEntity.ExpectsProviderRecommendations = true;
        }
        else if (string.Equals(promptEntity.PromptClass, "ProviderEvaluation", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(promptEntity.PromptClass, "Informational", StringComparison.OrdinalIgnoreCase))
        {
            promptEntity.MetricBucket = "AnswerReadiness";
            promptEntity.IsOrganicVisibilityEligible = false;
            promptEntity.VisibilityWeight = 0;
        }
    }

    private static bool ContainsBrand(string prompt, string businessName)
    {
        if (string.IsNullOrWhiteSpace(prompt) || string.IsNullOrWhiteSpace(businessName))
            return false;

        return prompt.Contains(businessName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksComparative(string prompt)
    {
        return prompt.Contains(" vs ", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains(" versus ", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains("compare", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains("better than", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains("alternative", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal Clamp01(decimal value)
    {
        return Math.Clamp(value, 0m, 1m);
    }

    private static string? NormalizePromptClass(string? value)
    {
        return NormalizeKnownValue(value, new[]
        {
            "Discovery",
            "Recommendation",
            "ProviderEvaluation",
            "Informational",
            "BrandedComparison",
            "BrandedResearch",
            "Navigational"
        });
    }

    private static string? NormalizeMetricBucket(string? value)
    {
        return NormalizeKnownValue(value, new[]
        {
            "OrganicVisibility",
            "AnswerReadiness",
            "BrandedPresence",
            "Excluded"
        });
    }

    private static string? NormalizeKnownValue(string? value, IEnumerable<string> allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var compact = value.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        return allowedValues.FirstOrDefault(v => string.Equals(
            v.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal),
            compact,
            StringComparison.OrdinalIgnoreCase));
    }

    private class EnrichmentResponse
    {
        public List<EnrichedPromptItem>? enrichedPrompts { get; set; }
    }

    private class EnrichedPromptItem
    {
        public string? id { get; set; }
        public string? promptClass { get; set; }
        public bool isBranded { get; set; }
        public bool isOrganicVisibilityEligible { get; set; }
        public bool expectsProviderRecommendations { get; set; }
        public bool expectsBrandMention { get; set; }
        public string? metricBucket { get; set; }
        public decimal visibilityWeight { get; set; }
        public string? scoringReason { get; set; }
        public decimal classificationConfidence { get; set; }
        public string? intent { get; set; }
        public string? persona { get; set; }
        public string? difficulty { get; set; }
        public string? estimatedInterestLevel { get; set; }
        public string? region { get; set; }
        public string? language { get; set; }
        public int commercialValue { get; set; }
        public string? topicValidation { get; set; }
        public string? buyerJourneyStage { get; set; }
    }
}
