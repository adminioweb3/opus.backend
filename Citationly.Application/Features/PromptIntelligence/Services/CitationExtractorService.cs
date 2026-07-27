using System.Text.RegularExpressions;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ICitationExtractorService
{
    IEnumerable<PromptCitation> ExtractCitations(Guid analysisId, string platform, string responseText, string? ownDomain);
}

/// <summary>
/// Real regex extraction of URLs actually present in a captured AI response — unlike everything
/// else citation-shaped in this codebase (the old markdown-link-only check in
/// VisibilityCalculatorService, and the entire separate "Citation Intelligence" subsystem, which
/// asks an LLM to invent plausible-sounding source names), this never fabricates a domain.
/// </summary>
public class CitationExtractorService : ICitationExtractorService
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s)""'>\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SocialDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "reddit.com", "x.com", "twitter.com", "linkedin.com", "facebook.com", "instagram.com",
    };

    public IEnumerable<PromptCitation> ExtractCitations(Guid analysisId, string platform, string responseText, string? ownDomain)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in UrlRegex.Matches(responseText))
        {
            var rawUrl = match.Value.TrimEnd('.', ',', ')', ']');
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) continue;

            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            if (!seen.Add(host)) continue;

            yield return new PromptCitation
            {
                PromptAnalysisId = analysisId,
                Platform = platform,
                Domain = host,
                Url = rawUrl,
                Category = CategoryFor(host, ownDomain),
            };
        }
    }

    private static string CategoryFor(string host, string? ownDomain)
    {
        if (!string.IsNullOrWhiteSpace(ownDomain) && host.Contains(ownDomain, StringComparison.OrdinalIgnoreCase))
            return "Owned";

        if (SocialDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
            return "Social";

        if (host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase)
            || host.Equals("wikipedia.org", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".wikipedia.org", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("arxiv.org", StringComparison.OrdinalIgnoreCase))
            return "Institution";

        return "Other";
    }
}
