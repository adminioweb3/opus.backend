using System.Text.RegularExpressions;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Assistant.Agents;

public sealed record ContentGroundingSource(
    Guid PageId,
    string Title,
    string Url,
    string Excerpt,
    int RelevanceScore);

public sealed record ContentPolicyCheck(
    string Key,
    string Label,
    string Status,
    string Message,
    bool Blocking);

public static partial class ContentExecutionGrounding
{
    private const int MaxExcerptLength = 3_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "into", "your", "their", "about",
        "create", "improve", "recommendation", "content", "page", "site", "using", "more"
    };

    public static IReadOnlyList<ContentGroundingSource> SelectSources(
        IEnumerable<ScrapedPage> pages,
        AgentRecommendation recommendation,
        int limit = 5)
    {
        var keywords = ExtractKeywords(
            $"{recommendation.Title} {recommendation.Summary} {recommendation.Rationale} " +
            $"{recommendation.TargetKey} {recommendation.ActionPlanJson}");

        return pages
            .Where(page => !string.IsNullOrWhiteSpace(page.MarkdownContent) || !string.IsNullOrWhiteSpace(page.Content))
            .Select(page =>
            {
                var body = page.MarkdownContent ?? page.Content ?? string.Empty;
                var haystack = $"{page.Title} {page.Description} {body}".ToLowerInvariant();
                var score = keywords.Sum(keyword => CountOccurrences(haystack, keyword));
                if (!string.IsNullOrWhiteSpace(recommendation.TargetKey) &&
                    haystack.Contains(recommendation.TargetKey.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    score += 10;
                }
                return new ContentGroundingSource(
                    page.Id,
                    string.IsNullOrWhiteSpace(page.Title) ? page.Url : page.Title!,
                    page.Url,
                    body.Length <= MaxExcerptLength ? body : body[..MaxExcerptLength],
                    score);
            })
            .OrderByDescending(source => source.RelevanceScore)
            .ThenBy(source => source.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 10))
            .ToList();
    }

    public static IReadOnlyList<ContentPolicyCheck> EvaluatePolicy(
        string title,
        string markdown,
        IReadOnlyList<ContentGroundingSource> sources)
    {
        var references = SourceReferenceRegex().Matches(markdown)
            .Select(match => int.TryParse(match.Groups[1].Value, out var value) ? value : 0)
            .Where(value => value > 0)
            .ToList();
        var invalidReferences = references.Where(reference => reference > sources.Count).Distinct().ToList();
        var wordCount = WordRegex().Matches(markdown).Count;

        return
        [
            new("knowledge_sources", "Knowledge Vault evidence",
                sources.Count > 0 ? "Passed" : "Failed",
                sources.Count > 0
                    ? $"{sources.Count} indexed source(s) were attached to this draft."
                    : "No indexed Knowledge Vault source was available; generation is blocked.",
                true),
            new("source_citations", "Grounded citations",
                references.Count > 0 && invalidReferences.Count == 0 ? "Passed" : "Failed",
                references.Count == 0
                    ? "The draft does not cite any supplied source."
                    : invalidReferences.Count > 0
                        ? $"The draft references unavailable source number(s): {string.Join(", ", invalidReferences)}."
                        : $"{references.Distinct().Count()} supplied source(s) are cited in the draft.",
                true),
            new("title", "Draft title",
                string.IsNullOrWhiteSpace(title) ? "Failed" : "Passed",
                string.IsNullOrWhiteSpace(title) ? "A title is required." : "A reviewable title is present.",
                true),
            new("review_depth", "Review depth",
                wordCount >= 250 ? "Passed" : "Warning",
                wordCount >= 250
                    ? $"The draft contains {wordCount} words."
                    : $"The draft contains {wordCount} words; a reviewer should confirm that it is sufficiently complete.",
                false),
            new("publish_gate", "Live publishing gate", "Passed",
                "The draft cannot be published by the agent until a manager approves a separate live-publishing request.",
                true)
        ];
    }

    public static bool HasBlockingFailure(IEnumerable<ContentPolicyCheck> checks) =>
        checks.Any(check => check.Blocking && check.Status == "Failed");

    public static IReadOnlyList<object> BuildReviewDiff(string markdown)
    {
        var sections = HeadingRegex().Matches(markdown)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
        if (sections.Count == 0) sections.Add("Draft body");

        return sections
            .Select(section => (object)new
            {
                changeType = "Added",
                section,
                before = (string?)null,
                after = $"New grounded section: {section}",
                rationale = "Created from the approved GEO recommendation and attached Knowledge Vault evidence."
            })
            .ToList();
    }

    private static IReadOnlyList<string> ExtractKeywords(string value) =>
        WordRegex().Matches(value)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(word => word.Length > 2 && !StopWords.Contains(word))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [GeneratedRegex(@"\[Source\s+(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceReferenceRegex();

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}'’-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^#{2,3}\s+(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();
}
