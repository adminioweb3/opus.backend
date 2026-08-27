using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ISentimentClassifierService
{
    Task<(string? Sentiment, string? Quote)> ClassifyAsync(Guid organizationId, string responseText, string brandName, CancellationToken ct);
}

/// <summary>
/// Same "single JSON-mode LLM call judges pos/neu/neg" idiom Brand Pulse already uses
/// (RunBrandPulseScanCommand.BuildPrompt/ParseJudgment), applied per-response instead of as an
/// org-wide summary. Unlike Brand Pulse, this does NOT fall back to a hardcoded percentage on
/// failure — for a single response, an unclassified (null) sentiment is more honest than a guess.
/// </summary>
public class SentimentClassifierService : ISentimentClassifierService
{
    private readonly IAiCompletionService _aiCompletionService;

    public SentimentClassifierService(IAiCompletionService aiCompletionService)
    {
        _aiCompletionService = aiCompletionService;
    }

    public async Task<(string? Sentiment, string? Quote)> ClassifyAsync(Guid organizationId, string responseText, string brandName, CancellationToken ct)
    {
        var deterministic = TryClassifyDeterministically(responseText, brandName);
        if (deterministic.Sentiment != null)
        {
            return deterministic;
        }

        const string systemPrompt =
            "You are a brand sentiment analyst. Based ONLY on the text provided, judge how it portrays the named brand. " +
            "Respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"sentiment\": \"pos\"|\"neu\"|\"neg\", " +
            "\"quote\": a short verbatim excerpt (under 140 characters) from the text that best supports your judgment. " +
            "Do not wrap in markdown.";

        var userPrompt = $"Brand: {brandName}\n\nText:\n{responseText}";

        try
        {
            var completion = await _aiCompletionService.CompleteAsync(
                organizationId,
                "prompt_intelligence.sentiment_classification",
                userPrompt,
                systemPrompt,
                requireJson: true,
                preferredProviderKey: "openai",
                ct);
            if (!completion.Success) return (null, null);

            var raw = StripFences(completion.Content);
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

    private static (string? Sentiment, string? Quote) TryClassifyDeterministically(string responseText, string brandName)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return (null, null);

        var text = responseText.ToLowerInvariant();
        var brand = brandName.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(brand) && !text.Contains(brand))
        {
            return ("neu", "");
        }

        var positiveHits = CountHits(text, PositiveTerms);
        var negativeHits = CountHits(text, NegativeTerms);
        if (positiveHits >= negativeHits + 2)
        {
            return ("pos", ExtractQuote(responseText, PositiveTerms));
        }

        if (negativeHits >= positiveHits + 2)
        {
            return ("neg", ExtractQuote(responseText, NegativeTerms));
        }

        return (null, null);
    }

    private static int CountHits(string text, string[] terms) => terms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string ExtractQuote(string responseText, string[] terms)
    {
        var sentences = responseText.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var sentence = sentences.FirstOrDefault(s => terms.Any(term => s.Contains(term, StringComparison.OrdinalIgnoreCase)))
            ?? sentences.FirstOrDefault()
            ?? string.Empty;
        return sentence.Length <= 140 ? sentence : sentence[..140];
    }

    private static readonly string[] PositiveTerms =
    {
        "best", "leading", "trusted", "reliable", "recommended", "popular", "strong", "excellent",
        "robust", "innovative", "accurate", "valuable", "effective", "top", "well-regarded"
    };

    private static readonly string[] NegativeTerms =
    {
        "poor", "weak", "unreliable", "expensive", "outdated", "limited", "bad", "risk", "risky",
        "inaccurate", "problem", "complaint", "slow", "hard to use", "not recommended"
    };

    private static string StripFences(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json")) s = s[7..];
        else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }
}
