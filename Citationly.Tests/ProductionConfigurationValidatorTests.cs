using Citationly.API.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Validate_DoesNotThrowOutsideProduction()
    {
        var config = new ConfigurationBuilder().Build();

        ProductionConfigurationValidator.Validate(config, "Development");
    }

    [Fact]
    public void Validate_ThrowsForMissingProductionCoreSecrets()
    {
        var config = new ConfigurationBuilder().Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(config, "Production"));

        Assert.Contains("ConnectionStrings:DefaultConnection", ex.Message);
        Assert.Contains("Firebase:ProjectId", ex.Message);
        Assert.Contains("Admin:JwtSigningKey", ex.Message);
    }

    [Fact]
    public void Validate_TreatsPlaceholdersAsMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "${DATABASE_URL}",
                ["Firebase:ProjectId"] = "${FIREBASE_PROJECT_ID}",
                ["Admin:JwtSigningKey"] = "${ADMIN_JWT_SIGNING_KEY}",
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(config, "Production"));

        Assert.Contains("ConnectionStrings:DefaultConnection", ex.Message);
        Assert.Contains("Firebase:ProjectId", ex.Message);
        Assert.Contains("Admin:JwtSigningKey", ex.Message);
    }

    [Fact]
    public void Validate_AllowsCompleteProductionCoreConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=citationly",
                ["Firebase:ProjectId"] = "citationly-prod",
                ["Admin:JwtSigningKey"] = "01234567890123456789012345678901",
            })
            .Build();

        ProductionConfigurationValidator.Validate(config, "Production");
    }

    [Fact]
    public void Validate_RequiresCashfreeConfigurationAndRedirectOriginsWhenBillingIsRequired()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=citationly",
                ["Firebase:ProjectId"] = "citationly-prod",
                ["Admin:JwtSigningKey"] = "01234567890123456789012345678901",
                ["Billing:RequireCashfree"] = "true",
                ["Cashfree:AppId"] = "${CASHFREE_APP_ID}",
                ["Cashfree:SecretKey"] = "${CASHFREE_SECRET_KEY}"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(config, "Production"));

        Assert.Contains("Cashfree:AppId", ex.Message);
        Assert.Contains("Cashfree:SecretKey", ex.Message);
        Assert.Contains("Cashfree:Plans:Pro:PlanId", ex.Message);
        Assert.Contains("Cashfree:Plans:Enterprise:PlanId", ex.Message);
        Assert.Contains("Billing:AllowedRedirectOrigins", ex.Message);
    }
}
