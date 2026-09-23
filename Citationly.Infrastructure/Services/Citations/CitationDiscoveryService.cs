using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Citations;

/// <summary>
/// Discovers citation opportunities from retrieved web evidence. It deliberately has no
/// LLM-only fallback because a plausible source name is not proof that the source exists or is
/// relevant to the supplied business.
/// </summary>
public class CitationDiscoveryService : ICitationDiscoveryService
{
    private readonly IWebEvidenceProvider _webEvidenceProvider;

    public CitationDiscoveryService(IWebEvidenceProvider webEvidenceProvider)
    {
        _webEvidenceProvider = webEvidenceProvider;
    }

    public async Task<List<CitationSource>> DiscoverCitationsAsync(
        Guid organizationId,
        string websiteUrl,
        string websiteProfileJson,
        string promptAnalysisJson,
        string platformScoresJson)
    {
        if (!_webEvidenceProvider.IsConfigured)
        {
            throw new InvalidOperationException(
                "Citation evidence is unavailable because Exa is not configured. Set EXA_API_KEY and enable Exa.");
        }

        var context = CompactContext($"{websiteProfileJson} {promptAnalysisJson}", 500);
        var queries = new[]
        {
            $"{websiteUrl} alternatives competitors reviews",
            $"{context} authoritative industry sources guides",
            $"{context} directories publications comparisons"
        };

        var observed = new Dictionary<string, (WebEvidenceItem Item, int Occurrences)>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var query in queries.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var result = await _webEvidenceProvider.SearchAsync(
                organizationId,
                new WebEvidenceQuery(query, ResultCount: 5));
            if (!result.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) errors.Add(result.ErrorMessage);
                continue;
            }

            foreach (var item in result.Items)
            {
                var key = NormalizeUrl(item.Url);
                if (observed.TryGetValue(key, out var existing))
                    observed[key] = (existing.Item, existing.Occurrences + 1);
                else
                    observed[key] = (item, 1);
            }
        }

        if (observed.Count == 0 && errors.Count > 0)
            throw new InvalidOperationException($"Exa returned no citation evidence: {errors[0]}");

        var rank = 0;
        return observed.Values
            .OrderByDescending(value => value.Occurrences)
            .ThenByDescending(value => value.Item.HighlightScores.DefaultIfEmpty(0).Max())
            .Select(value =>
            {
                rank++;
                var domain = Uri.TryCreate(value.Item.Url, UriKind.Absolute, out var uri) ? uri.Host : value.Item.Url;
                var relevance = value.Item.HighlightScores.DefaultIfEmpty(0).Average();
                return new CitationSource
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Rank = rank,
                    Source = value.Item.Url,
                    Category = CategorizeDomain(domain),
                    Reason = $"Observed by Exa for {value.Occurrences} relevant quer{(value.Occurrences == 1 ? "y" : "ies")}: {value.Item.Title}",
                    AuthorityScore = 0,
                    InfluenceScore = 0,
                    CitationFrequency = Math.Clamp(value.Occurrences * 25, 0, 100),
                    CompetitorCoverage = 0,
                    OpportunityScore = Math.Clamp((int)Math.Round(relevance * 100), 0, 100),
                    MentionProbability = 0,
                    IsEnriched = false,
                    CreatedAt = DateTime.UtcNow
                };
            })
            .ToList();
    }

    private static string CompactContext(string value, int maxLength)
    {
        var compact = string.Join(' ', value
            .Replace('{', ' ')
            .Replace('}', ' ')
            .Replace('[', ' ')
            .Replace(']', ' ')
            .Replace('"', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value.Trim();
        return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath.TrimEnd('/')}".ToLowerInvariant();
    }

    private static string CategorizeDomain(string domain)
    {
        var value = domain.ToLowerInvariant();
        if (value.Contains("reddit") || value.Contains("quora")) return "Community";
        if (value.Contains("g2.") || value.Contains("capterra") || value.Contains("trustpilot")) return "Review Platform";
        if (value.Contains("github") || value.Contains("docs.")) return "Documentation";
        return "Observed Web Source";
    }
}
