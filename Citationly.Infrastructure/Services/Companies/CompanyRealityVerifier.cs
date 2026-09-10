using System.Net;
using System.Text.RegularExpressions;
using Citationly.Application.Interfaces.Companies;
using Citationly.Domain.Utils;

namespace Citationly.Infrastructure.Services.Companies;

public sealed partial class CompanyRealityVerifier : ICompanyRealityVerifier
{
    private const int MaxResponseLength = 300_000;
    private const int MaxRedirects = 3;

    private static readonly string[] BlockedHosts =
    {
        "linkedin.com",
        "facebook.com",
        "instagram.com",
        "x.com",
        "twitter.com",
        "crunchbase.com",
        "pitchbook.com",
        "wikipedia.org",
        "yelp.com",
        "g2.com",
        "clutch.co",
        "zoominfo.com",
        "forbes.com"
    };

    private static readonly string[] PlaceholderMarkers =
    {
        "domain for sale",
        "this domain is parked",
        "coming soon",
        "under construction",
        "website is not available",
        "buy this domain",
        "domain expired",
        "parked free",
        "future home of"
    };

    private static readonly string[] BusinessMarkers =
    {
        "services",
        "products",
        "solutions",
        "customers",
        "industries",
        "about us",
        "contact",
        "pricing",
        "company",
        "platform",
        "consulting",
        "software"
    };

    private readonly HttpClient _httpClient;

    public CompanyRealityVerifier(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CompanyVerificationResult> VerifyAsync(
        string companyName,
        string website,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(companyName) ||
            string.IsNullOrWhiteSpace(website))
        {
            return Invalid("Company name or website is empty.");
        }

        if (!TryCreateUri(website, out var uri))
            return Invalid("Website is not a valid URL.");

        if (!IsAllowedCompanyWebsite(uri, out var hostReason))
            return Invalid(hostReason);

        string html;
        Uri finalUri;

        try
        {
            var fetched = await FetchHtmlAsync(uri, cancellationToken);
            if (!fetched.Success)
                return Invalid(fetched.Reason ?? "Website verification failed.");

            html = fetched.Html ?? string.Empty;
            finalUri = fetched.FinalUri ?? uri;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return Invalid($"Website could not be reached: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(html))
            return Invalid("Website returned an empty response.");

        if (html.Length > MaxResponseLength)
            html = html[..MaxResponseLength];

        var text = HtmlToText(html).ToLowerInvariant();

        foreach (var marker in PlaceholderMarkers)
        {
            if (text.Contains(marker, StringComparison.Ordinal))
                return Invalid($"Website appears to be parked or inactive: '{marker}'.");
        }

        if (text.Length < 200)
            return Invalid("Website contains too little readable business content.");

        var companyTokens = SignificantTokens(companyName);
        var hostTokens = SignificantTokens(DomainNormalizer.Normalize(finalUri.Host).Replace('.', ' '));

        var containsCompanyIdentity =
            companyTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)) ||
            hostTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

        if (!containsCompanyIdentity)
            return Invalid("Website does not appear to belong to the proposed company.");

        var businessMarkerCount = BusinessMarkers.Count(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase));

        if (businessMarkerCount < 2)
            return Invalid("Website does not contain enough evidence of an active business.");

        return new CompanyVerificationResult(
            IsVerified: true,
            NormalizedDomain: DomainNormalizer.Normalize(finalUri.Host),
            Reason: null);
    }

    private async Task<FetchResult> FetchHtmlAsync(Uri startUri, CancellationToken cancellationToken)
    {
        var uri = startUri;

        for (var redirect = 0; redirect <= MaxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd(
                "CitationlyCompanyVerifier/1.0 (+https://citationly.com)");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects)
                    return FetchResult.Fail("Website redirected too many times.");

                var location = response.Headers.Location;
                if (location == null)
                    return FetchResult.Fail("Website returned a redirect without a destination.");

                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!IsAllowedCompanyWebsite(uri, out var reason))
                    return FetchResult.Fail(reason);

                continue;
            }

            if (!response.IsSuccessStatusCode)
                return FetchResult.Fail($"Website returned HTTP {(int)response.StatusCode}.");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType != null &&
                !mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase) &&
                !mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                return FetchResult.Fail($"Website returned unsupported content type '{mediaType}'.");
            }

            return FetchResult.Ok(await response.Content.ReadAsStringAsync(cancellationToken), uri);
        }

        return FetchResult.Fail("Website redirected too many times.");
    }

    private static bool TryCreateUri(string website, out Uri uri)
    {
        var value = website.Trim();

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        return Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
               uri.Scheme is "http" or "https" &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static bool IsAllowedCompanyWebsite(Uri uri, out string reason)
    {
        var host = DomainNormalizer.Normalize(uri.Host);

        if (IsBlockedHost(host))
        {
            reason = $"'{host}' is a directory, social network, or company database.";
            return false;
        }

        if (IsUnsafeHost(host))
        {
            reason = $"'{host}' is not a public company website host.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsBlockedHost(string host)
    {
        return BlockedHosts.Any(blocked =>
            host.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{blocked}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnsafeHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal ||
               address.IsIPv6SiteLocal ||
               address.Equals(IPAddress.IPv6Loopback);
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = ScriptStyleOrNoscriptRegex().Replace(html, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutScripts, " ");

        return WhitespaceRegex().Replace(withoutTags, " ").Trim();
    }

    private static IEnumerable<string> SignificantTokens(string value)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the",
            "inc",
            "llc",
            "ltd",
            "company",
            "co",
            "corporation",
            "corp",
            "group",
            "solutions",
            "services",
            "technology",
            "technologies"
        };

        return TokenRegex()
            .Matches(value.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => !ignored.Contains(token))
            .Distinct();
    }

    private static CompanyVerificationResult Invalid(string reason)
    {
        return new CompanyVerificationResult(
            IsVerified: false,
            NormalizedDomain: null,
            Reason: reason);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private sealed record FetchResult(bool Success, string? Html, Uri? FinalUri, string? Reason)
    {
        public static FetchResult Ok(string html, Uri finalUri) => new(true, html, finalUri, null);

        public static FetchResult Fail(string reason) => new(false, null, null, reason);
    }

    [GeneratedRegex("<(script|style|noscript)[^>]*>[\\s\\S]*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptStyleOrNoscriptRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("[a-z0-9]{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}
