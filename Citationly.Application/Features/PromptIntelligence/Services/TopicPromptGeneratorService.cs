using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ITopicPromptGeneratorService
{
    Task<List<string>> GeneratePromptsAsync(string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null);
}

/// <summary>
/// Generates brand-aware prompts that real prospects would ask AI search engines — scoped to one
/// topic and the organization's own brand context, so analysis runs return meaningful visibility
/// scores instead of 0 for every generic SaaS/tech question.
/// </summary>
public class TopicPromptGeneratorService : ITopicPromptGeneratorService
{
    private readonly IOpenAiService _openAiService;

    public TopicPromptGeneratorService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<string>> GeneratePromptsAsync(string topicName, int count, CancellationToken ct, string? brandName = null, string? brandWebsite = null)
    {
        const string systemPrompt = "You are an expert AI Search Prompt Generator for a brand visibility and AEO (Answer Engine Optimization) tool. Your goal is to generate prompts that real prospects would search for, and that could plausibly surface the evaluated brand in an AI answer.";

        var brandContext = !string.IsNullOrWhiteSpace(brandName)
            ? $"\nBrand being tracked: '{brandName}' (website: {brandWebsite ?? "unknown"}).\nGenerate prompts that someone in this brand's target market would realistically ask — where the brand might organically appear in an AI answer if they are well-positioned in this niche."
            : "";

        var userPrompt = $@"Generate {count} realistic, distinct prompts that potential customers would ask AI search engines about the topic '{topicName}'.{brandContext}
Each prompt should:
- Sound like a genuine conversational question under 25 words
- Be specific enough that a real niche player (not just mega-brands) could be recommended
- NOT be generic 'What is X?' questions — instead ask things like 'best X for Y', 'who offers X in Y space', 'how to do X for Y use case'
Respond with ONLY JSON: {{""prompts"": [string, ...]}}. Do not wrap in markdown.";

        var result = new List<string>();
        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            raw = StripFences(raw);
            using var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("prompts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
                }
            }
        }
        catch
        {
            // Leave the result empty on failure — no fabricated prompts.
        }

        return result;
    }

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json")) s = s[7..];
        else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}
