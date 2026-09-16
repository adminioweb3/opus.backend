using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    private const long GlobalCallsPerMinute = 600;
    private const long TenantCallsPerMinute = 60;
    private readonly IAiRateLimitStore _rateLimitStore;
    private readonly IEntitlementService _entitlements;

    public AiUsageLimiter(IAiRateLimitStore rateLimitStore, IEntitlementService entitlements)
    {
        _rateLimitStore = rateLimitStore;
        _entitlements = entitlements;
    }

    public async Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMinutes(1);

        var global = await _rateLimitStore.TryConsumeAsync(
            "ai:global",
            periodStart,
            periodEnd,
            GlobalCallsPerMinute,
            cancellationToken: cancellationToken);
        if (!global.IsWithinLimit)
        {
            throw new InvalidOperationException($"Global AI rate limit exceeded for {operationName}. Try again shortly.");
        }

        if (!organizationId.HasValue) return;

        var spendQuota = await _entitlements.CheckQuotaAsync(
            organizationId.Value,
            "ai_spend_micro_usd_per_day",
            cancellationToken);
        if (!spendQuota.IsWithinLimit)
        {
            var spendLimit = spendQuota.Limit.HasValue ? spendQuota.Limit.Value / 1_000_000m : 0m;
            throw new InvalidOperationException(
                $"Daily AI spend quota exceeded for {operationName}. Configured limit: ${spendLimit:F2}.");
        }

        var providerSpendMetric = GetProviderMonthlySpendMetric(operationName);
        if (providerSpendMetric is not null)
        {
            var providerSpendQuota = await _entitlements.CheckQuotaAsync(
                organizationId.Value,
                providerSpendMetric,
                cancellationToken);
            if (!providerSpendQuota.IsWithinLimit)
            {
                var limit = providerSpendQuota.Limit.HasValue ? providerSpendQuota.Limit.Value / 1_000_000m : 0m;
                throw new InvalidOperationException(
                    $"Monthly provider spend quota exceeded for {operationName}. Configured limit: ${limit:F2}.");
            }
        }

        var tenant = await _rateLimitStore.TryConsumeAsync(
            $"ai:tenant:{organizationId.Value:N}",
            periodStart,
            periodEnd,
            TenantCallsPerMinute,
            cancellationToken: cancellationToken);
        if (!tenant.IsWithinLimit)
        {
            throw new InvalidOperationException($"Organization AI rate limit exceeded for {operationName}. Try again shortly.");
        }

        var quota = await _entitlements.TryConsumeUsageAsync(
            organizationId.Value,
            "ai_calls_per_day",
            cancellationToken: cancellationToken);
        if (!quota.IsWithinLimit)
        {
            var limitText = quota.Limit.HasValue ? quota.Limit.Value.ToString() : "configured";
            throw new InvalidOperationException($"Daily AI call quota exceeded for {operationName}. Current usage: {quota.CurrentUsage}/{limitText}.");
        }
    }

    public async Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default)
    {
        if (!organizationId.HasValue || !costUsd.HasValue || costUsd.Value <= 0) return;

        var microUsd = Math.Max(1, (long)Math.Ceiling(costUsd.Value * 1_000_000m));
        var daily = await _entitlements.TryConsumeUsageAsync(
            organizationId.Value,
            "ai_spend_micro_usd_per_day",
            microUsd,
            cancellationToken);
        if (!daily.IsWithinLimit)
            throw new InvalidOperationException($"Daily AI spend reservation failed for {operationName}.");

        var providerSpendMetric = GetProviderMonthlySpendMetric(operationName);
        if (providerSpendMetric is null) return;

        var monthly = await _entitlements.TryConsumeUsageAsync(
            organizationId.Value,
            providerSpendMetric,
            microUsd,
            cancellationToken);
        if (!monthly.IsWithinLimit)
            throw new InvalidOperationException($"Monthly provider spend reservation failed for {operationName}.");
    }

    private static string? GetProviderMonthlySpendMetric(string operationName)
    {
        if (operationName.Contains("openrouter", StringComparison.OrdinalIgnoreCase))
            return "openrouter_spend_micro_usd_per_month";
        if (operationName.Contains("exa", StringComparison.OrdinalIgnoreCase))
            return "exa_spend_micro_usd_per_month";
        return null;
    }
}
