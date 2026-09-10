using System.Text.RegularExpressions;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface IVisibilityCalculatorService
{
    IEnumerable<PromptMention> ExtractMentions(
        Guid analysisId,
        IEnumerable<PromptResponse> responses,
        string brandName,
        IEnumerable<string> competitors);

    (PromptVisibility Visibility, IEnumerable<PromptMention> Mentions, IEnumerable<CompetitorComparison> CompetitorComparisons) CalculateVisibilityMetrics(
        Guid analysisId,
        IEnumerable<PromptResponse> responses,
        string brandName,
        IEnumerable<string> competitors,
        IEnumerable<PromptMention>? classifiedMentions = null,
        IEnumerable<PromptCitation>? citations = null);
}

/// <summary>
/// Calculates prompt visibility from captured response evidence.
///
/// Methodology v4 (Peec-compatible brand metrics):
/// - mention frequency: percentage of successful samples containing the brand;
/// - visibility score: the same response-level brand mention percentage;
/// - average position: mean 1-based mention order among tracked brands (0 when absent);
/// - share of voice: brand mention count divided by mentions of all tracked brands;
/// - citation share: owned-domain citations divided by every extracted citation.
///
/// A previous implementation treated the character offset of a name in prose as its "position"
/// and derived visibility from that offset. That number was not a search/recommendation rank.
/// </summary>
public class VisibilityCalculatorService : IVisibilityCalculatorService
{
    public IEnumerable<PromptMention> ExtractMentions(
        Guid analysisId,
        IEnumerable<PromptResponse> responses,
        string brandName,
        IEnumerable<string> competitors)
    {
        var entities = new[] { (Name: brandName, IsBrand: true) }
            .Concat(competitors
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => (Name: name, IsBrand: false)))
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .ToList();

        foreach (var response in responses)
        {
            var text = response.ResponseText ?? string.Empty;
            var found = entities
                .Select(entity => (
                    entity.Name,
                    entity.IsBrand,
                    Index: (entity.IsBrand ? BuildBrandAliases(entity.Name) : new[] { entity.Name })
                        .Select(alias => FindEntity(text, alias))
                        .Where(index => index >= 0)
                        .DefaultIfEmpty(-1)
                        .Min()))
                .Where(entity => entity.Index >= 0)
                .OrderBy(entity => entity.Index)
                .ToList();

            for (var mentionIndex = 0; mentionIndex < found.Count; mentionIndex++)
            {
                var entity = found[mentionIndex];
                var index = entity.Index;

                var snippetStart = Math.Max(0, index - 60);
                var snippetLength = Math.Min(text.Length - snippetStart, entity.Name.Length + 120);
                yield return new PromptMention
                {
                    PromptAnalysisId = analysisId,
                    PromptResponseId = response.Id,
                    Platform = response.Platform,
                    EntityName = entity.Name,
                    IsBrand = entity.IsBrand,
                    ContextSnippet = text.Substring(snippetStart, snippetLength).Replace("\n", " "),
                    // 1-based order among tracked brands in this captured answer.
                    Position = mentionIndex + 1,
                };
            }
        }
    }

    public (PromptVisibility Visibility, IEnumerable<PromptMention> Mentions, IEnumerable<CompetitorComparison> CompetitorComparisons) CalculateVisibilityMetrics(
        Guid analysisId,
        IEnumerable<PromptResponse> responses,
        string brandName,
        IEnumerable<string> competitors,
        IEnumerable<PromptMention>? classifiedMentions = null,
        IEnumerable<PromptCitation>? citations = null)
    {
        var responseList = responses.Where(response => !response.IsError).ToList();
        var competitorList = competitors
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var mentions = (classifiedMentions ?? ExtractMentions(analysisId, responseList, brandName, competitorList)).ToList();
        var citationList = citations?.ToList() ?? new List<PromptCitation>();
        var sampleCount = responseList.Count;

        var brandMentions = mentions.Where(mention => mention.IsBrand).ToList();
        var mentionedSamples = brandMentions
            .Where(mention => mention.PromptResponseId.HasValue)
            .Select(mention => mention.PromptResponseId!.Value)
            .Distinct()
            .Count();
        var mentionFrequency = Percentage(mentionedSamples, sampleCount);

        var brandPositions = brandMentions
            .Where(mention => mention.Position > 0)
            .Select(mention => mention.Position)
            .ToList();
        var averagePosition = brandPositions.Count == 0
            ? 0
            : (int)Math.Round(brandPositions.Average(), MidpointRounding.AwayFromZero);
        var visibilityScore = mentionFrequency;

        var competitorMentionCounts = competitorList.ToDictionary(
            name => name,
            name => mentions
                .Where(mention => !mention.IsBrand
                    && string.Equals(mention.EntityName, name, StringComparison.OrdinalIgnoreCase))
                .Count(),
            StringComparer.OrdinalIgnoreCase);

        var totalTrackedBrandMentions = brandMentions.Count + competitorMentionCounts.Values.Sum();
        var shareOfVoice = totalTrackedBrandMentions == 0
            ? 0
            : Percentage(brandMentions.Count, totalTrackedBrandMentions);

        var ownedCitations = citationList.Count(citation => string.Equals(citation.Category, "Owned", StringComparison.OrdinalIgnoreCase));
        var citationShare = Percentage(ownedCitations, citationList.Count);

        var competitorScores = competitorList.ToDictionary(
            name => name,
            name => Percentage(
                mentions
                    .Where(mention => !mention.IsBrand
                        && string.Equals(mention.EntityName, name, StringComparison.OrdinalIgnoreCase)
                        && mention.PromptResponseId.HasValue)
                    .Select(mention => mention.PromptResponseId!.Value)
                    .Distinct()
                    .Count(),
                sampleCount),
            StringComparer.OrdinalIgnoreCase);
        // An all-zero evidence panel has no winner. Zero is the API's explicit unranked value.
        var visibilityRank = totalTrackedBrandMentions == 0
            ? 0
            : 1 + competitorScores.Values.Count(score => score > visibilityScore);

        var visibility = new PromptVisibility
        {
            PromptAnalysisId = analysisId,
            OverallVisibilityScore = visibilityScore,
            VisibilityRank = visibilityRank,
            MentionFrequency = mentionFrequency,
            AveragePosition = averagePosition,
            ShareOfVoice = shareOfVoice,
            CitationCount = ownedCitations,
            CitationShare = citationShare,
            CompetitorCount = competitorList.Count,
            SampleCount = sampleCount,
            MethodologyVersion = "prompt-visibility:v4-mention-share",
        };

        var comparisons = competitorList.Select(name => new CompetitorComparison
        {
            PromptAnalysisId = analysisId,
            CompetitorName = name,
            VisibilityScore = competitorScores[name],
            ShareOfVoice = totalTrackedBrandMentions == 0
                ? 0
                : Percentage(competitorMentionCounts[name], totalTrackedBrandMentions),
            MissingTopicsJson = "[]",
        }).ToList();

        return (visibility, mentions, comparisons);
    }

    private static int FindEntity(string text, string entityName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(entityName)) return -1;
        var match = Regex.Match(
            text,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(entityName.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Index : -1;
    }

    private static IReadOnlyCollection<string> BuildBrandAliases(string brandName)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = brandName.Trim();
        if (trimmed.Length == 0) return aliases;
        aliases.Add(trimmed);

        // Website titles are often persisted as "Brand | Tagline" or "Brand - Product".
        // A model naturally says only "Brand", which must still count as the same entity.
        foreach (var separator in new[] { " | ", " — ", " – ", " - " })
        {
            var separatorIndex = trimmed.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex >= 2)
            {
                var candidate = trimmed[..separatorIndex].Trim();
                if (IsSafeAlias(candidate)) aliases.Add(candidate);
            }
        }

        var withoutCorporateSuffix = Regex.Replace(
            trimmed,
            @"\s+(?:pvt\.?\s+ltd\.?|private\s+limited|incorporated|corporation|company|limited|llc|ltd\.?|inc\.?|corp\.?)$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
        if (IsSafeAlias(withoutCorporateSuffix)) aliases.Add(withoutCorporateSuffix);

        return aliases;
    }

    private static bool IsSafeAlias(string candidate)
    {
        if (candidate.Length < 3) return false;
        var genericSingleWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "data", "solutions", "services", "systems", "technology", "technologies", "group", "company"
        };
        return candidate.Contains(' ') || !genericSingleWords.Contains(candidate);
    }

    private static int Percentage(int numerator, int denominator) =>
        denominator <= 0 ? 0 : ClampPercentage((double)numerator / denominator * 100d);

    private static int ClampPercentage(double value) =>
        Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
}
