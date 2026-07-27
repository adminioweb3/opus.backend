namespace Citationly.Domain.Utils;

/// <summary>
/// Canonicalizes any raw URL/domain string down to a bare, lowercase host with no scheme,
/// "www.", port, path, query, fragment, or trailing dot — e.g. "https://WWW.Acme.com/pricing?x=1"
/// and "acme.com" both normalize to "acme.com". This is the join key for Company.NormalizedDomain,
/// the unique identity that lets the Company Knowledge Graph dedupe across every organization.
/// </summary>
public static class DomainNormalizer
{
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var candidate = url.Trim();
        if (!candidate.Contains("://")) candidate = "https://" + candidate;

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            var host = uri.Host.ToLowerInvariant().TrimEnd('.');
            return host.StartsWith("www.") ? host[4..] : host;
        }

        // Fallback for inputs Uri can't parse (rare) — strip manually.
        var s = url.Trim().ToLowerInvariant();
        s = s.Replace("https://", "").Replace("http://", "");
        if (s.StartsWith("www.")) s = s[4..];
        var cut = s.IndexOfAny(['/', '?', '#']);
        return (cut >= 0 ? s[..cut] : s).TrimEnd('.');
    }
}
