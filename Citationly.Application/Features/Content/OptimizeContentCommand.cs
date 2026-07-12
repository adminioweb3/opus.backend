using System.Text.Json;
using System.Text.RegularExpressions;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Content;

public class OptimizeContentCommand : IRequest<ContentOptimization?>
{
    public Guid OrganizationId { get; set; }
    public Guid ContentDraftId { get; set; }
    public string PrimaryKeyword { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int Depth { get; set; }
}

public class OptimizeContentCommandHandler : IRequestHandler<OptimizeContentCommand, ContentOptimization?>
{
    private readonly IContentDraftRepository _draftRepository;
    private readonly IContentOptimizationRepository _optimizationRepository;
    private readonly IOpenAiService _openAiService;

    public OptimizeContentCommandHandler(
        IContentDraftRepository draftRepository,
        IContentOptimizationRepository optimizationRepository,
        IOpenAiService openAiService)
    {
        _draftRepository = draftRepository;
        _optimizationRepository = optimizationRepository;
        _openAiService = openAiService;
    }

    public async Task<ContentOptimization?> Handle(OptimizeContentCommand request, CancellationToken cancellationToken)
    {
        var draft = await _draftRepository.GetByIdAsync(request.ContentDraftId);
        if (draft == null || draft.OrganizationId != request.OrganizationId) return null;

        var readability = CalculateReadability(draft.Content);
        var keywordDensity = CalculateKeywordDensity(draft.Content, request.PrimaryKeyword);

        const string systemPrompt =
            "You are an expert SEO and content strategist. Given a content draft, its primary keyword, a computed " +
            "readability score, and computed keyword density, respond with ONLY a JSON object with EXACTLY these keys: " +
            "\"seoScore\" (integer 0-100, overall SEO strength given keyword usage, structure, and headings), " +
            "\"humanizedScore\" (integer 0-100, how natural and human the writing feels), " +
            "\"aiScore\" (integer 0-100, estimated likelihood this reads as AI-generated — lower is better), " +
            "\"recommendations\" (array of up to 5 objects with \"title\" and \"meta\" describing priority improvements), " +
            "\"internalLinkSuggestions\" (array of up to 4 short strings suggesting internal links to add), " +
            "\"citationRecommendations\" (array of up to 4 short strings suggesting citations or proof points to add), " +
            "\"optimizedContent\" (string: the FULL rewritten draft in Markdown, addressing the goal/audience/notes and " +
            "incorporating the recommendations, preserving the original H1 title unless changing it improves SEO).";

        var prompt =
            $"PRIMARY KEYWORD: {request.PrimaryKeyword}\n" +
            $"COMPUTED READABILITY (Flesch Reading Ease, 0-100, higher = easier): {readability:F0}\n" +
            $"COMPUTED KEYWORD DENSITY: {keywordDensity:F2}%\n" +
            $"OPTIMIZATION GOAL: {request.Goal}\n" +
            $"TARGET AUDIENCE: {request.Audience}\n" +
            $"OPTIMIZATION DEPTH: {request.Depth}% (higher = more aggressive rewriting)\n" +
            (string.IsNullOrWhiteSpace(request.Notes) ? "" : $"NOTES: {request.Notes}\n") +
            $"\nCONTENT DRAFT:\n{draft.Content}";

        var raw = await _openAiService.GenerateContentAsync(prompt, systemPrompt, requireJson: true);
        var parsed = ParseOptimization(raw);

        var optimization = new ContentOptimization
        {
            ContentDraftId = draft.Id,
            OrganizationId = request.OrganizationId,
            SeoScore = parsed.SeoScore,
            ReadabilityScore = (int)Math.Round(readability),
            HumanizedScore = parsed.HumanizedScore,
            AiScore = parsed.AiScore,
            KeywordDensity = keywordDensity,
            PrimaryKeyword = request.PrimaryKeyword,
            RecommendationsJson = parsed.RecommendationsJson,
            InternalLinksJson = parsed.InternalLinksJson,
            CitationRecsJson = parsed.CitationRecsJson,
            OptimizedContent = parsed.OptimizedContent
        };

        return await _optimizationRepository.CreateAsync(optimization);
    }

    private static double CalculateReadability(string content)
    {
        var words = Regex.Matches(content, @"[A-Za-z']+");
        var sentenceCount = Math.Max(1, Regex.Matches(content, @"[.!?]+").Count);
        if (words.Count == 0) return 0;

        var syllables = 0;
        foreach (Match m in words) syllables += CountSyllables(m.Value);

        var score = 206.835 - 1.015 * ((double)words.Count / sentenceCount) - 84.6 * ((double)syllables / words.Count);
        return Math.Max(0, Math.Min(100, score));
    }

    private static int CountSyllables(string word)
    {
        word = word.ToLowerInvariant();
        var count = 0;
        var prevWasVowel = false;
        foreach (var c in word)
        {
            var isVowel = "aeiouy".IndexOf(c) >= 0;
            if (isVowel && !prevWasVowel) count++;
            prevWasVowel = isVowel;
        }
        if (word.EndsWith("e") && count > 1) count--;
        return Math.Max(1, count);
    }

    private static double CalculateKeywordDensity(string content, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return 0;

        var totalWords = Regex.Matches(content, @"[A-Za-z']+").Count;
        if (totalWords == 0) return 0;

        var occurrences = Regex.Matches(content, Regex.Escape(keyword), RegexOptions.IgnoreCase).Count;
        var keywordWordCount = Math.Max(1, keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        return Math.Round((double)(occurrences * keywordWordCount) / totalWords * 100, 2);
    }

    private record ParsedOptimization(
        int SeoScore,
        int HumanizedScore,
        int AiScore,
        string RecommendationsJson,
        string InternalLinksJson,
        string CitationRecsJson,
        string OptimizedContent);

    private static ParsedOptimization ParseOptimization(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            return new ParsedOptimization(
                SeoScore: root.TryGetProperty("seoScore", out var s) ? s.GetInt32() : 0,
                HumanizedScore: root.TryGetProperty("humanizedScore", out var h) ? h.GetInt32() : 0,
                AiScore: root.TryGetProperty("aiScore", out var a) ? a.GetInt32() : 0,
                RecommendationsJson: root.TryGetProperty("recommendations", out var r) ? r.GetRawText() : "[]",
                InternalLinksJson: root.TryGetProperty("internalLinkSuggestions", out var l) ? l.GetRawText() : "[]",
                CitationRecsJson: root.TryGetProperty("citationRecommendations", out var c) ? c.GetRawText() : "[]",
                OptimizedContent: root.TryGetProperty("optimizedContent", out var o) ? o.GetString() ?? string.Empty : string.Empty);
        }
        catch (Exception)
        {
            return new ParsedOptimization(0, 0, 0, "[]", "[]", "[]", raw);
        }
    }
}
