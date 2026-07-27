using System.Text.Json;
using System.Text.Json.Serialization;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptDiscoveryService : IPromptDiscoveryService
{
    private readonly IOpenAiService _openAiService;

    // Four concurrent batches, optimized for 40 total prompts:
    // Each batch generates 10 prompts for better API performance.
    // Batches split by buyer-journey stage for diversity.
    // Parallel execution keeps wall-clock time fast.
    private static readonly (string Focus, int Min, int Max)[] Batches =
    {
        ("category and problem-awareness questions (e.g. \"what is the best tool for X\", \"how do I solve Y\")", 10, 10),
        ("direct comparison and alternative-seeking questions (e.g. \"X vs Y\", \"alternatives to X\")", 10, 10),
        ("pricing, ROI, and validation questions (e.g. cost, free trial, is it worth it)", 10, 10),
        ("implementation, integration, and support questions (e.g. setup, onboarding, compatibility)", 10, 10),
    };

    public PromptDiscoveryService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<AiSearchPrompt>> DiscoverPromptsAsync(Guid organizationId, string websiteProfile)
    {
        const string systemPrompt = "You are an expert AI Search Prompt Generator. Focus purely on discovery.";

        var batchTasks = Batches.Select(b => DiscoverBatchAsync(websiteProfile, systemPrompt, b.Focus, b.Min, b.Max));
        var batchResults = await Task.WhenAll(batchTasks);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<AiSearchPrompt>();

        foreach (var items in batchResults)
        {
            foreach (var p in items)
            {
                var text = (p.prompt ?? "").Trim();
                if (text.Length == 0 || !seen.Add(text)) continue;

                var options = JsonOptions;
                entities.Add(new AiSearchPrompt
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    QueryString = text,
                    SearchEngine = "Google", // Default
                    Topic = p.topic ?? "General",
                    IsEnriched = false,
                    GeneratedAt = DateTime.UtcNow,
                    RawJson = JsonSerializer.Serialize(p, options),
                });
            }
        }

        return entities;
    }

    private async Task<List<DiscoveryPromptItem>> DiscoverBatchAsync(string websiteProfile, string systemPrompt, string focus, int min, int max)
    {
        var userPrompt = $@"
Your task is to generate realistic prompts that potential customers would ask AI search engines when searching for products or services similar to the provided business.

## Input
Website Profile
{websiteProfile}

## Objective
Generate between {min} and {max} unique, realistic prompts that prospective customers would naturally ask AI assistants, focused specifically on: {focus}

## Instructions
1. Use the Website Profile to understand the business, services, target audience, and pain points.
2. Every prompt must fit the focus area above — do not drift into unrelated question types.
3. Descriptions should sound like real users asking AI assistants. They must feel conversational.
4. Maximum prompt length: 25 words.
5. Provide ONLY Prompt ID, Prompt, and Topic.
6. NO additional metadata, NO persona, NO intent, NO region, NO difficulty, NO search estimate.
7. Output MUST remain below 1000 tokens.
8. CRITICAL: Return at least {min} prompts. Return exactly the following JSON structure. Do NOT include markdown. Do NOT wrap in ```json.

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

        string responseContent;
        try
        {
            responseContent = await _openAiService.GenerateContentAsync(
                prompt: userPrompt,
                systemPrompt: systemPrompt,
                requireJson: true,
                model: "gpt-4o-mini");
        }
        catch (Exception ex)
        {
            // One batch failing shouldn't sink the other three — the org still gets a partial,
            // real prompt set rather than nothing.
            Console.WriteLine($"[Discovery] Batch '{focus}' failed: {ex.Message}");
            return new List<DiscoveryPromptItem>();
        }

        responseContent = StripFences(responseContent);

        try
        {
            var result = JsonSerializer.Deserialize<DiscoveryResponse>(responseContent, JsonOptions);
            return result?.prompts ?? new List<DiscoveryPromptItem>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Discovery] Batch '{focus}' JSON parse failed: {ex.Message}");
            return new List<DiscoveryPromptItem>();
        }
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json"))
        {
            s = s[7..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        if (s.StartsWith("```"))
        {
            s = s[3..];
            if (s.EndsWith("```")) s = s[..^3];
        }
        return s;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

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
