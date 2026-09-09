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
        await _entitlements.ConsumeUsageAsync(
            organizationId.Value,
            "ai_spend_micro_usd_per_day",
            microUsd,
            cancellationToken);
    }
}
