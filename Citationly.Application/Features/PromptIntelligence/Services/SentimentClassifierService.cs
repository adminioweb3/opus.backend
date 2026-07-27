using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ISentimentClassifierService
{
    Task<(string? Sentiment, string? Quote)> ClassifyAsync(string responseText, string brandName, CancellationToken ct);
}

/// <summary>
/// Same "single JSON-mode LLM call judges pos/neu/neg" idiom Brand Pulse already uses
/// (RunBrandPulseScanCommand.BuildPrompt/ParseJudgment), applied per-response instead of as an
/// org-wide summary. Unlike Brand Pulse, this does NOT fall back to a hardcoded percentage on
/// failure — for a single response, an unclassified (null) sentiment is more honest than a guess.
/// </summary>
public class SentimentClassifierService : ISentimentClassifierService
{
    private readonly IOpenAiService _openAiService;

    public SentimentClassifierService(IOpenAiService openAiService)
    {
        _openAiService = openAiService;
    }

    public async Task<(string? Sentiment, string? Quote)> ClassifyAsync(string responseText, string brandName, CancellationToken ct)
    {
        const string systemPrompt =
            "You are a brand sentiment analyst. Based ONLY on the text provided, judge how it portrays the named brand. " +
            "Respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"sentiment\": \"pos\"|\"neu\"|\"neg\", " +
            "\"quote\": a short verbatim excerpt (under 140 characters) from the text that best supports your judgment. " +
            "Do not wrap in markdown.";

        var userPrompt = $"Brand: {brandName}\n\nText:\n{responseText}";

        try
        {
            var raw = await _openAiService.GenerateContentAsync(userPrompt, systemPrompt, requireJson: true);
            raw = StripFences(raw);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            var sentiment = root.TryGetProperty("sentiment", out var s) ? s.GetString() : null;
            var quote = root.TryGetProperty("quote", out var q) ? q.GetString() : null;

            if (sentiment is not ("pos" or "neu" or "neg")) return (null, null);
            return (sentiment, quote);
        }
        catch
        {
            return (null, null);
        }
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
