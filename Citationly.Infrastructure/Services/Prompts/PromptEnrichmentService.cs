using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptEnrichmentService : IPromptEnrichmentService
{
    private readonly IOpenAiService _openAiService;
    private readonly IWebsiteRepository _websiteRepository;

    public PromptEnrichmentService(IOpenAiService openAiService, IWebsiteRepository websiteRepository)
    {
        _openAiService = openAiService;
        _websiteRepository = websiteRepository;
    }

    public async Task EnrichPromptsBatchAsync(List<AiSearchPrompt> batch)
    {
        if (batch == null || !batch.Any()) return;

        var systemPrompt = "You are an expert AI Search Prompt Enriched Metadata Analyzer.";

        // Prepare batch json for prompt
        var batchInput = batch.Select(p => new { Id = p.Id, Prompt = p.QueryString, Topic = p.Topic }).ToList();
        var batchJson = JsonSerializer.Serialize(batchInput);

        var userPrompt = $@"
Your task is to enrich the following batch of AI Search Prompts with metadata.

## Input Batch
{batchJson}

## Objective
For each prompt in the batch, generate the following metadata:
- Intent: (Informational, Commercial, Transactional, Navigational, Comparative, Educational)
- Persona: (Startup Founder, CTO, CEO, Product Manager, etc.)
- Difficulty: (Easy, Medium, Hard)
- MonthlySearchEstimate: (Very Low, Low, Medium, High, Very High)
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
      ""intent"": """",
      ""persona"": """",
      ""difficulty"": """",
      ""monthlySearchEstimate"": """",
      ""region"": """",
      ""language"": """",
      ""commercialValue"": 0,
      ""topicValidation"": """",
      ""buyerJourneyStage"": """"
    }}
  ]
}}
";

        var responseContent = await _openAiService.GenerateContentAsync(
            prompt: userPrompt,
            systemPrompt: systemPrompt,
            requireJson: true,
            model: "gpt-4o-mini");

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
                        promptEntity.Intent = enriched.intent;
                        promptEntity.Persona = enriched.persona;
                        promptEntity.Difficulty = enriched.difficulty;
                        promptEntity.MonthlySearchEstimate = enriched.monthlySearchEstimate;
                        promptEntity.Region = enriched.region;
                        promptEntity.Language = enriched.language;
                        promptEntity.CommercialValue = enriched.commercialValue;
                        promptEntity.TopicValidation = enriched.topicValidation;
                        promptEntity.BuyerJourneyStage = enriched.buyerJourneyStage;
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

    private class EnrichmentResponse
    {
        public List<EnrichedPromptItem>? enrichedPrompts { get; set; }
    }

    private class EnrichedPromptItem
    {
        public string? id { get; set; }
        public string? intent { get; set; }
        public string? persona { get; set; }
        public string? difficulty { get; set; }
        public string? monthlySearchEstimate { get; set; }
        public string? region { get; set; }
        public string? language { get; set; }
        public int commercialValue { get; set; }
        public string? topicValidation { get; set; }
        public string? buyerJourneyStage { get; set; }
    }
}
