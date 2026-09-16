using System.Security.Cryptography;
using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class IntegrationCredentialProtectorTests
{
    [Fact]
    public void Protect_EncryptsAndRoundTripsCredential()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Integrations:EncryptionKey"] = key })
            .Build();
        var protector = new IntegrationCredentialProtector(configuration);

        var protectedValue = protector.Protect("editor:application-password");

        Assert.StartsWith("enc:v1:", protectedValue);
        Assert.DoesNotContain("application-password", protectedValue);
        Assert.Equal("editor:application-password", protector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_RejectsMissingEncryptionKey()
    {
        var protector = new IntegrationCredentialProtector(new ConfigurationBuilder().Build());
        var exception = Assert.Throws<InvalidOperationException>(() => protector.Protect("secret"));
        Assert.Contains("Integrations__EncryptionKey", exception.Message);
    }
}
