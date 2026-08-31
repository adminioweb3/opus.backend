using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services;

/// <summary>Verifies Cashfree's Base64 HMAC-SHA256 signature over timestamp plus the exact raw body.</summary>
public sealed class CashfreeWebhookSignatureVerifier
{
    private readonly string? _secretKey;

    public CashfreeWebhookSignatureVerifier(IConfiguration configuration)
    {
        _secretKey = ResolveConfigured(configuration["Cashfree:SecretKey"]);
    }

    public bool IsConfigured => _secretKey is not null;

    public bool Verify(string rawBody, string timestamp, string signature)
    {
        if (_secretKey is null || string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(signature)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + rawBody));
        try
        {
            var received = Convert.FromBase64String(signature);
            return CryptographicOperations.FixedTimeEquals(expected, received);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? ResolveConfigured(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("${", StringComparison.Ordinal) ? null : value;
}
