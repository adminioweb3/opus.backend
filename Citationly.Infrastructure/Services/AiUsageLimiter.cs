using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    private const string DailyQuotaMetricKey = "ai_calls_per_day";
    private const string DailySpendMetricKey = "ai_spend_micro_usd_per_day";

    private static readonly TimeSpan WindowSize = TimeSpan.FromHours(1);
    private const int TenantLimitPerWindow = 80;
    private const int GlobalLimitPerWindow = 500;

    private readonly IEntitlementService _entitlements;
    private readonly IAiRateLimitStore _rateLimitStore;

    public AiUsageLimiter(IEntitlementService entitlements, IAiRateLimitStore rateLimitStore)
    {
        _entitlements = entitlements;
        _rateLimitStore = rateLimitStore;
    }

    public async Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default)
    {
        var tenantKey = $"tenant:{organizationId?.ToString() ?? "anonymous"}";
        await EnsureWithinLimitAsync(tenantKey, TenantLimitPerWindow, operationName, cancellationToken);
        await EnsureWithinLimitAsync("global", GlobalLimitPerWindow, operationName, cancellationToken);

        if (organizationId is Guid orgId)
        {
            var quota = await _entitlements.TryConsumeUsageAsync(orgId, DailyQuotaMetricKey, cancellationToken: cancellationToken);
            if (!quota.IsWithinLimit)
            {
                throw new InvalidOperationException(
                    $"Daily AI usage limit reached for {operationName} ({quota.CurrentUsage}/{quota.Limit}). " +
                    "Upgrade your plan or try again tomorrow.");
            }

            var spendQuota = await _entitlements.CheckQuotaAsync(orgId, DailySpendMetricKey, cancellationToken);
            if (!spendQuota.IsWithinLimit)
            {
                throw new InvalidOperationException(
                    $"Daily AI spend limit reached for {operationName} ({FormatMicroUsd(spendQuota.CurrentUsage)}/{FormatMicroUsd(spendQuota.Limit)}). " +
                    "Upgrade your plan or try again tomorrow.");
            }
        }
    }

    public async Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default)
    {
        if (organizationId is not Guid orgId || costUsd is null || costUsd <= 0) return;

        var microUsd = (long)Math.Ceiling(costUsd.Value * 1_000_000m);
        if (microUsd <= 0) return;

        await _entitlements.ConsumeUsageAsync(orgId, DailySpendMetricKey, microUsd, cancellationToken);
    }

    private async Task EnsureWithinLimitAsync(string key, int limit, string operationName, CancellationToken cancellationToken)
    {
        var (periodStart, periodEnd) = GetCurrentWindow();
        var quota = await _rateLimitStore.TryConsumeAsync(key, periodStart, periodEnd, limit, cancellationToken: cancellationToken);
        if (!quota.IsWithinLimit)
        {
            throw new InvalidOperationException($"AI usage limit exceeded for {operationName}. Try again later.");
        }
    }

    private static (DateTime PeriodStart, DateTime PeriodEnd) GetCurrentWindow()
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        return (periodStart, periodStart.Add(WindowSize));
    }

    private static string FormatMicroUsd(long? microUsd)
    {
        return microUsd is null ? "unlimited" : $"${microUsd.Value / 1_000_000m:0.######}";
    }
}
