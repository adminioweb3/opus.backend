using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services;

/// <summary>Allows Stripe to redirect only to explicitly configured first-party origins.</summary>
public sealed class BillingRedirectUrlValidator
{
    private readonly HashSet<string> _allowedOrigins;

    public BillingRedirectUrlValidator(IConfiguration configuration)
    {
        _allowedOrigins = configuration
            .GetSection("Billing:AllowedRedirectOrigins")
            .Get<string[]>()?
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && IsHttpsOrLocalHttp(uri))
            .Select(value => new Uri(value).GetLeftPart(UriPartial.Authority).TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public string Validate(string? value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsHttpsOrLocalHttp(uri))
            throw new InvalidOperationException($"{fieldName} must be an absolute HTTPS URL.");

        var origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        if (!_allowedOrigins.Contains(origin))
            throw new InvalidOperationException($"{fieldName} is not an allowed billing redirect origin.");

        return uri.ToString();
    }

    private static bool IsHttpsOrLocalHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps ||
        (uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)));
}
