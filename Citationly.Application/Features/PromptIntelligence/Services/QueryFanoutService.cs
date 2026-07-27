using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IQueryFanoutService
{
    Task<List<PromptFanout>> GenerateFanoutsAsync(Guid questionId, string promptText, CancellationToken ct);
}

/// <summary>
/// LLM-generated illustrative sub-query variations for a broad prompt — not literally intercepted
/// per-engine query expansions (no vendor exposes their real internal query-fanout mechanics via
/// API), but real, freshly-generated text describing plausible follow-up searches, not canned demo
/// strings.
/// </summary>
public class QueryFanoutService : IQueryFanoutService
{
    private readonly IOpenAiService _openAiService;

    public QueryFanoutService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<List<PromptFanout>> GenerateFanoutsAsync(Guid questionId, string promptText, CancellationToken ct)
    {
        const string systemPrompt =
            "You help analyze how AI search engines break a broad question into more specific sub-queries. " +
            "Given a prompt, imagine 8 to 12 realistic, more specific search queries a person researching this " +
            "topic might follow up with. Respond with ONLY JSON: " +
            "{\"fanouts\": [{\"query\": string, \"engine\": \"ChatGPT\"|\"Perplexity\"|\"Gemini\"}]}. Do not wrap in markdown.";

        var userPrompt = $"Original prompt: {promptText}";

        var result = new List<PromptFanout>();
        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            raw = StripFences(raw);
            using var doc = JsonDocument.Parse(raw);

            if (doc.RootElement.TryGetProperty("fanouts", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var query = item.TryGetProperty("query", out var q) ? q.GetString() : null;
                    var engine = item.TryGetProperty("engine", out var e) ? e.GetString() : "ChatGPT";
                    if (string.IsNullOrWhiteSpace(query)) continue;

                    result.Add(new PromptFanout
                    {
                        PromptQuestionId = questionId,
                        FanoutText = query.Trim(),
                        Engine = string.IsNullOrWhiteSpace(engine) ? "ChatGPT" : engine,
                    });
                }
            }
        }
        catch
        {
            // Leave the result empty on failure — no fabricated fanouts.
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
