using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services;
using Xunit;

namespace Citationly.Tests;

public class AiUsageLimiterTests
{
    [Fact]
    public async Task EnsureWithinLimitsAsync_DoesNotBlockWhenQuotaWouldBeExceeded()
    {
        var entitlements = new StubEntitlementService
        {
            TryConsumeResult = new UsageQuotaStatus(false, 50, 50),
            SpendQuotaResult = new UsageQuotaStatus(false, 100000, 100000)
        };
        var limiter = new AiUsageLimiter(entitlements);

        await limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation");

        Assert.Equal(0, entitlements.ConsumeCalls);
        Assert.Equal(0, entitlements.TryConsumeCalls);
        Assert.Equal(0, entitlements.CheckQuotaCalls);
    }

    [Fact]
    public async Task RecordEstimatedCostAsync_RecordsSpendForReporting()
    {
        var entitlements = new StubEntitlementService();
        var limiter = new AiUsageLimiter(entitlements);

        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), 0.000123m, "test.operation");

        Assert.Equal(1, entitlements.ConsumeCalls);
    }

    [Fact]
    public async Task RecordEstimatedCostAsync_IgnoresMissingOrganizationOrCost()
    {
        var entitlements = new StubEntitlementService();
        var limiter = new AiUsageLimiter(entitlements);

        await limiter.RecordEstimatedCostAsync(null, 0.000123m, "test.operation");
        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), null, "test.operation");
        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), 0, "test.operation");

        Assert.Equal(0, entitlements.ConsumeCalls);
    }

    private sealed class StubEntitlementService : IEntitlementService
    {
        public UsageQuotaStatus TryConsumeResult { get; init; } = new(true, 1, 10);
        public UsageQuotaStatus SpendQuotaResult { get; init; } = new(true, 0, null);
        public int CheckQuotaCalls { get; private set; }
        public int ConsumeCalls { get; private set; }
        public int TryConsumeCalls { get; private set; }

        public Task<string> GetPlanKeyAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult("Trial");

        public Task<bool> CanUseFeatureAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<long?> GetPlanLimitValueAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(10);

        public Task<UsageQuotaStatus> CheckQuotaAsync(Guid organizationId, string metricKey, CancellationToken cancellationToken = default)
        {
            CheckQuotaCalls++;
            return Task.FromResult(SpendQuotaResult);
        }

        public Task<UsageQuotaStatus> TryConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
        {
            TryConsumeCalls++;
            return Task.FromResult(TryConsumeResult);
        }

        public Task ConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            return Task.CompletedTask;
        }
    }
}
