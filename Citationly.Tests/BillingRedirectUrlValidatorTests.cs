using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class BillingRedirectUrlValidatorTests
{
    private static BillingRedirectUrlValidator CreateValidator() => new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:AllowedRedirectOrigins:0"] = "https://app.citationly.ai",
                ["Billing:AllowedRedirectOrigins:1"] = "http://localhost:3000"
            })
            .Build());

    [Fact]
    public void Validate_AllowsConfiguredOrigin()
    {
        var result = CreateValidator().Validate("https://app.citationly.ai/dashboard/settings?billing=success", "SuccessUrl");

        Assert.Equal("https://app.citationly.ai/dashboard/settings?billing=success", result);
    }

    [Fact]
    public void Validate_RejectsUnconfiguredOrigin()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate("https://attacker.example/return", "ReturnUrl"));

        Assert.Contains("not an allowed", ex.Message);
    }

    [Fact]
    public void Validate_RejectsNonHttpsNonLocalUrl()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CreateValidator().Validate("http://app.citationly.ai/dashboard", "CancelUrl"));

        Assert.Contains("HTTPS", ex.Message);
    }
}
