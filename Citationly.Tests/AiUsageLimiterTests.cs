using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services;
using Xunit;

namespace Citationly.Tests;

public class AiUsageLimiterTests
{
    [Fact]
    public async Task EnsureWithinLimitsAsync_UsesAtomicUsageReservation()
    {
        var entitlements = new StubEntitlementService
        {
            TryConsumeResult = new UsageQuotaStatus(true, 1, 10),
            SpendQuotaResult = new UsageQuotaStatus(true, 0, 10_000_000)
        };
        var rateLimits = new StubAiRateLimitStore();
        var limiter = new AiUsageLimiter(entitlements, rateLimits);

        await limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation");

        Assert.Equal(2, rateLimits.TryConsumeCalls);
        Assert.Equal(1, entitlements.TryConsumeCalls);
        Assert.Equal(0, entitlements.ConsumeCalls);
        Assert.Equal(1, entitlements.CheckQuotaCalls);
    }

    [Fact]
    public async Task EnsureWithinLimitsAsync_ThrowsWhenAtomicUsageReservationFails()
    {
        var entitlements = new StubEntitlementService
        {
            TryConsumeResult = new UsageQuotaStatus(false, 10, 10),
            SpendQuotaResult = new UsageQuotaStatus(true, 0, 10_000_000)
        };
        var limiter = new AiUsageLimiter(entitlements, new StubAiRateLimitStore());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation"));

        Assert.Contains("Daily AI usage limit reached", ex.Message);
        Assert.Equal(1, entitlements.TryConsumeCalls);
        Assert.Equal(0, entitlements.ConsumeCalls);
    }

    [Fact]
    public async Task EnsureWithinLimitsAsync_ThrowsWhenSharedHourlyLimitFails()
    {
        var entitlements = new StubEntitlementService
        {
            TryConsumeResult = new UsageQuotaStatus(true, 1, 10),
            SpendQuotaResult = new UsageQuotaStatus(true, 0, 10_000_000)
        };
        var rateLimits = new StubAiRateLimitStore
        {
            TryConsumeResult = new UsageQuotaStatus(false, 80, 80)
        };
        var limiter = new AiUsageLimiter(entitlements, rateLimits);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation"));

        Assert.Contains("AI usage limit exceeded", ex.Message);
        Assert.Equal(1, rateLimits.TryConsumeCalls);
        Assert.Equal(0, entitlements.TryConsumeCalls);
    }

    private sealed class StubAiRateLimitStore : IAiRateLimitStore
    {
        public UsageQuotaStatus TryConsumeResult { get; init; } = new(true, 1, 80);
        public int TryConsumeCalls { get; private set; }

        public Task<UsageQuotaStatus> TryConsumeAsync(
            string scopeKey,
            DateTime periodStartUtc,
            DateTime periodEndUtc,
            long limit,
            long amount = 1,
            CancellationToken cancellationToken = default)
        {
            TryConsumeCalls++;
            return Task.FromResult(TryConsumeResult);
        }
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
