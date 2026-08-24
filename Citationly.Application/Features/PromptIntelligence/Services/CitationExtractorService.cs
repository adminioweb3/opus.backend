using System.Text.RegularExpressions;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ICitationExtractorService
{
    IEnumerable<PromptCitation> ExtractCitations(
        Guid analysisId,
        string platform,
        string responseText,
        string? ownDomain,
        IReadOnlyCollection<string>? competitorDomains = null);
}

/// <summary>
/// Real regex extraction of URLs actually present in a captured AI response — unlike everything
/// else citation-shaped in this codebase (the old markdown-link-only check in
/// VisibilityCalculatorService, and the entire separate "Citation Intelligence" subsystem, which
/// asks an LLM to invent plausible-sounding source names), this never fabricates a domain.
///
/// Phase 3 C1: classification is a best-effort domain/keyword heuristic, not a licensed domain-
/// intelligence dataset (no Moz/Ahrefs/SEMrush integration exists anywhere in this codebase, per
/// CITATIONLY_PRODUCT_AUDIT.md). The curated lists below are a starting point, not exhaustive —
/// expand them as real citation data reveals gaps. Anything not matched falls to "Unknown" rather
/// than being guessed.
/// </summary>
public class CitationExtractorService : ICitationExtractorService
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s)""'>\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> SocialDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "x.com", "twitter.com", "linkedin.com", "facebook.com", "instagram.com", "tiktok.com", "youtube.com",
    };

    private static readonly HashSet<string> CommunityDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "reddit.com", "quora.com", "stackoverflow.com", "stackexchange.com", "news.ycombinator.com",
        "discourse.org", "discord.com",
    };

    private static readonly HashSet<string> ReviewPlatformDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "g2.com", "capterra.com", "trustradius.com", "trustpilot.com", "getapp.com", "softwareadvice.com",
        "yelp.com", "glassdoor.com", "sitejabber.com", "producthunt.com",
    };

    private static readonly HashSet<string> DirectoryDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "crunchbase.com", "clutch.co", "goodfirms.co", "designrush.com", "expertise.com", "yellowpages.com",
    };

    private static readonly HashSet<string> MarketplaceDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "amazon.com", "shopify.com", "apps.shopify.com", "chromewebstore.google.com",
        "marketplace.atlassian.com", "workspace.google.com", "appsource.microsoft.com",
    };

    private static readonly HashSet<string> ReferenceDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "wikipedia.org", "wiktionary.org", "britannica.com",
    };

    private static readonly HashSet<string> AcademicDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "arxiv.org", "scholar.google.com", "researchgate.net", "ncbi.nlm.nih.gov", "jstor.org",
        "sciencedirect.com", "springer.com", "ieee.org",
    };

    // A handful of high-traffic tech/business editorial outlets commonly cited in AI answers.
    // Not exhaustive - this bucket is meant to catch the common case, with "Unknown" as the
    // honest fallback for anything not recognized rather than a guessed classification.
    private static readonly HashSet<string> EditorialMediaDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "techcrunch.com", "forbes.com", "businessinsider.com", "wired.com", "theverge.com",
        "cnbc.com", "bloomberg.com", "reuters.com", "wsj.com", "nytimes.com", "bbc.com",
        "zdnet.com", "venturebeat.com", "arstechnica.com", "engadget.com", "gartner.com",
    };

    public IEnumerable<PromptCitation> ExtractCitations(
        Guid analysisId,
        string platform,
        string responseText,
        string? ownDomain,
        IReadOnlyCollection<string>? competitorDomains = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedCompetitorDomains = new HashSet<string>(
            (competitorDomains ?? Array.Empty<string>()).Select(NormalizeHost).Where(h => !string.IsNullOrEmpty(h)),
            StringComparer.OrdinalIgnoreCase);

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
                Category = CategoryFor(host, uri.AbsolutePath, ownDomain, normalizedCompetitorDomains),
            };
        }
    }

    private static string NormalizeHost(string domainOrUrl)
    {
        if (string.IsNullOrWhiteSpace(domainOrUrl)) return string.Empty;
        var candidate = domainOrUrl.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : string.Empty;
    }

    private static bool MatchesAny(string host, HashSet<string> domains) =>
        domains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));

    private static string CategoryFor(string host, string path, string? ownDomain, HashSet<string> competitorDomains)
    {
        if (!string.IsNullOrWhiteSpace(ownDomain) && host.Contains(ownDomain, StringComparison.OrdinalIgnoreCase))
            return "Owned";

        if (competitorDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
            return "Competitor";

        if (host.EndsWith(".gov", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".mil", StringComparison.OrdinalIgnoreCase))
            return "Government";

        if (host.EndsWith(".edu", StringComparison.OrdinalIgnoreCase) || MatchesAny(host, AcademicDomains))
            return "Academic";

        if (MatchesAny(host, ReferenceDomains))
            return "Reference";

        if (MatchesAny(host, CommunityDomains))
            return "Community";

        if (MatchesAny(host, SocialDomains))
            return "Social";

        if (MatchesAny(host, ReviewPlatformDomains))
            return "ReviewPlatform";

        if (MatchesAny(host, DirectoryDomains))
            return "Directory";

        if (MatchesAny(host, MarketplaceDomains))
            return "Marketplace";

        if (MatchesAny(host, EditorialMediaDomains))
            return "EditorialMedia";

        // Path-based heuristic for the citing site's own documentation - a docs subdomain or
        // /docs//api path is a reasonable signal regardless of which domain it's on.
        if (host.StartsWith("docs.", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("developer.", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/", StringComparison.OrdinalIgnoreCase))
            return "Documentation";

        return "Unknown";
    }
}
