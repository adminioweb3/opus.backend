using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    private const string DailySpendMetricKey = "ai_spend_micro_usd_per_day";

    private readonly IEntitlementService _entitlements;

    public AiUsageLimiter(IEntitlementService entitlements)
    {
        _entitlements = entitlements;
    }

    public Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default)
    {
        if (organizationId is not Guid orgId || costUsd is null || costUsd <= 0) return;

        var microUsd = (long)Math.Ceiling(costUsd.Value * 1_000_000m);
        if (microUsd <= 0) return;

        await _entitlements.ConsumeUsageAsync(orgId, DailySpendMetricKey, microUsd, cancellationToken);
    }
}
