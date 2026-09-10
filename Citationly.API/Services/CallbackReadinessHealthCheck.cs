using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Citationly.API.Services;

public sealed class CallbackReadinessHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public CallbackReadinessHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var requireCashfree = _configuration.GetValue<bool>("Billing:RequireCashfree");
        var missing = new List<string>();

        if (requireCashfree)
        {
            RequireConfigured("Cashfree:AppId", missing);
            RequireConfigured("Cashfree:SecretKey", missing);
            RequireConfigured("Cashfree:Plans:Pro:PlanId", missing);
            RequireConfigured("Cashfree:Plans:Enterprise:PlanId", missing);

            var allowedOrigins = _configuration.GetSection("Billing:AllowedRedirectOrigins").Get<string[]>() ?? Array.Empty<string>();
            if (allowedOrigins.Length == 0)
            {
                missing.Add("Billing:AllowedRedirectOrigins");
            }
        }

        var data = new Dictionary<string, object>
        {
            ["cashfreeRequired"] = requireCashfree,
            ["cashfreeWebhookPath"] = "/api/Billing/webhook",
            ["missingConfiguration"] = missing.ToArray()
        };

        if (!requireCashfree)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "Cashfree billing callbacks are not required by current configuration.",
                data));
        }

        return missing.Count == 0
            ? Task.FromResult(HealthCheckResult.Healthy("Cashfree callback prerequisites are configured.", data))
            : Task.FromResult(HealthCheckResult.Unhealthy("Cashfree callback prerequisites are missing.", data: data));
    }

    private void RequireConfigured(string key, ICollection<string> missing)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("${", StringComparison.Ordinal))
        {
            missing.Add(key);
        }
    }
}
