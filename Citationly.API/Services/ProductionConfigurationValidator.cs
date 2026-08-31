namespace Citationly.API.Services;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        if (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) || 
            string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var missing = new List<string>();
        Require(configuration.GetConnectionString("DefaultConnection"), "ConnectionStrings:DefaultConnection", missing);
        Require(configuration["Firebase:ProjectId"], "Firebase:ProjectId", missing);
        Require(configuration["Admin:JwtSigningKey"], "Admin:JwtSigningKey", missing, minLength: 32);

        if (configuration.GetValue<bool>("Billing:RequireCashfree"))
        {
            Require(configuration["Cashfree:AppId"], "Cashfree:AppId", missing);
            Require(configuration["Cashfree:SecretKey"], "Cashfree:SecretKey", missing);
            Require(configuration["Cashfree:Plans:Pro:PlanId"], "Cashfree:Plans:Pro:PlanId", missing);
            Require(configuration["Cashfree:Plans:Enterprise:PlanId"], "Cashfree:Plans:Enterprise:PlanId", missing);
            if (!configuration.GetSection("Billing:AllowedRedirectOrigins").GetChildren().Any())
            {
                missing.Add("Billing:AllowedRedirectOrigins");
            }
        }

        if (configuration.GetValue<bool>("AI:RequireOpenAI"))
        {
            Require(configuration["OpenAI:ApiKey"], "OpenAI:ApiKey", missing);
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Configuration is incomplete for non-local environment: " + string.Join(", ", missing));
        }
    }

    private static void Require(string? value, string key, List<string> missing, int minLength = 1)
    {
        var resolved = Resolve(value);
        if (resolved == null || resolved.Length < minLength)
        {
            missing.Add(key);
        }
    }

    private static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("${", StringComparison.Ordinal) ? null : value;
    }
}
