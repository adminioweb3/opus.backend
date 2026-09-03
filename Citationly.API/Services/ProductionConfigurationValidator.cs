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
        Require(configuration["Admin:Username"], "Admin:Username", missing);
        RequireBcryptHash(configuration["Admin:PasswordHash"], "Admin:PasswordHash", missing);

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

    private static void RequireBcryptHash(string? value, string key, List<string> missing)
    {
        var resolved = Resolve(value);
        if (resolved == null)
        {
            missing.Add(key);
            return;
        }

        if (!IsBcryptHash(resolved))
        {
            missing.Add($"{key} (valid bcrypt hash)");
        }
    }

    private static bool IsBcryptHash(string value)
    {
        if (value.Length != 60)
        {
            return false;
        }

        if (!(value.StartsWith("$2a$", StringComparison.Ordinal) ||
              value.StartsWith("$2b$", StringComparison.Ordinal) ||
              value.StartsWith("$2y$", StringComparison.Ordinal)))
        {
            return false;
        }

        if (!char.IsDigit(value[4]) || !char.IsDigit(value[5]) || value[6] != '$')
        {
            return false;
        }

        return value[7..].All(c =>
            c is >= 'A' and <= 'Z' ||
            c is >= 'a' and <= 'z' ||
            c is >= '0' and <= '9' ||
            c == '.' ||
            c == '/');
    }

    private static string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.StartsWith("${", StringComparison.Ordinal) ? null : value;
    }
}
