using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptDiscoveryService : IPromptDiscoveryService
{
    private readonly IOpenAiService _openAiService;

    public PromptDiscoveryService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<AiSearchPrompt>> DiscoverPromptsAsync(Guid organizationId, string websiteProfile)
    {
        var systemPrompt = "You are an expert AI Search Prompt Generator. Focus purely on discovery.";

        var userPrompt = $@"
Your task is to generate realistic prompts that potential customers would ask AI search engines when searching for products or services similar to the provided business.

## Input
Website Profile
{websiteProfile}

## Objective
Generate between 50 and 100 unique, realistic prompts that prospective customers would naturally ask AI assistants.

## Instructions
1. Use the Website Profile to understand the business, services, target audience, and pain points.
2. Generate prompts covering various topics related to the business.
3. Descriptions should sound like real users asking AI assistants. They must feel conversational.
4. Maximum prompt length: 25 words.
5. Provide ONLY Prompt ID, Prompt, and Topic.
6. NO additional metadata, NO persona, NO intent, NO region, NO difficulty, NO search estimate.
7. Output MUST remain below 2000 tokens.
8. CRITICAL: Return a minimum of 50 prompts. Return exactly the following JSON structure. Do NOT include markdown. Do NOT wrap in ```json.

Return exactly this schema:
{{
  ""prompts"": [
    {{
      ""promptId"": ""PROMPT-001"",
      ""prompt"": """",
      ""topic"": """"
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

        var result = JsonSerializer.Deserialize<DiscoveryResponse>(responseContent, options);

        var entities = new List<AiSearchPrompt>();

        if (result != null && result.prompts != null)
        {
            foreach (var p in result.prompts)
            {
                var rawJson = JsonSerializer.Serialize(p, options);

                entities.Add(new AiSearchPrompt
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    QueryString = p.prompt ?? "",
                    SearchEngine = "Google", // Default
                    Topic = p.topic ?? "General",
                    IsEnriched = false,
                    GeneratedAt = DateTime.UtcNow,
                    RawJson = rawJson
                });
            }
        }

        return entities;
    }

    private class DiscoveryResponse
    {
        public List<DiscoveryPromptItem>? prompts { get; set; }
    }

    private class DiscoveryPromptItem
    {
        public string? promptId { get; set; }
        public string? prompt { get; set; }
        public string? topic { get; set; }
    }
}
