using System.Security.Cryptography;
using System.Text;
using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class CashfreeWebhookSignatureVerifierTests
{
    private const string Secret = "cashfree-test-secret";
    private static CashfreeWebhookSignatureVerifier CreateVerifier() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cashfree:SecretKey"] = Secret
        }).Build());

    [Fact]
    public void Verify_AcceptsMatchingRawBodySignature()
    {
        const string timestamp = "1746427759733";
        const string body = "{\"type\":\"SUBSCRIPTION_STATUS_CHANGED\"}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + body)));

        Assert.True(CreateVerifier().Verify(body, timestamp, signature));
    }

    [Fact]
    public void Verify_RejectsTamperedRawBody()
    {
        const string timestamp = "1746427759733";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "{}")));

        Assert.False(CreateVerifier().Verify("{\"changed\":true}", timestamp, signature));
    }
}
