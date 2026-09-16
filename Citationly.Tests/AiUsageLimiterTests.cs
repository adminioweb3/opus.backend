using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services;
using Xunit;

namespace Citationly.Tests;

public class AiUsageLimiterTests
{
    [Fact]
    public async Task EnsureWithinLimitsAsync_ReservesDailyCallQuota_WhenWithinLimits()
    {
        var entitlements = new StubEntitlementService();
        var rateLimits = new StubRateLimitStore();
        var limiter = new AiUsageLimiter(rateLimits, entitlements);

        await limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation");

        Assert.Equal(2, rateLimits.TryConsumeCalls);
        Assert.Equal(1, entitlements.TryConsumeCalls);
        Assert.Equal(1, entitlements.CheckQuotaCalls);
        Assert.Equal("ai_calls_per_day", entitlements.LastTryConsumeMetric);
    }

    [Fact]
    public async Task EnsureWithinLimitsAsync_Throws_WhenDailyCallQuotaWouldBeExceeded()
    {
        var entitlements = new StubEntitlementService
        {
            TryConsumeResult = new UsageQuotaStatus(false, 50, 50)
        };
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation"));

        Assert.Contains("Daily AI call quota exceeded", ex.Message);
        Assert.Equal(1, entitlements.TryConsumeCalls);
    }

    [Fact]
    public async Task EnsureWithinLimitsAsync_Throws_WhenDailySpendQuotaWasReached()
    {
        var entitlements = new StubEntitlementService
        {
            SpendQuotaResult = new UsageQuotaStatus(false, 1_000_000, 1_000_000)
        };
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "test.operation"));

        Assert.Contains("Daily AI spend quota exceeded", ex.Message);
        Assert.Equal(0, entitlements.TryConsumeCalls);
    }

    [Fact]
    public async Task EnsureWithinLimitsAsync_Throws_WhenProviderMonthlySpendQuotaWasReached()
    {
        var entitlements = new StubEntitlementService
        {
            ProviderSpendQuotaResult = new UsageQuotaStatus(false, 5_000_000, 5_000_000)
        };
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            limiter.EnsureWithinLimitsAsync(Guid.NewGuid(), "provider:openrouter:chat"));

        Assert.Contains("Monthly provider spend quota exceeded", ex.Message);
        Assert.Equal(2, entitlements.CheckQuotaCalls);
        Assert.Equal(0, entitlements.TryConsumeCalls);
    }

    [Fact]
    public async Task RecordEstimatedCostAsync_RecordsSpendOnlyAfterProviderReportsCost()
    {
        var entitlements = new StubEntitlementService();
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), 0.000123m, "test.operation");

        Assert.Equal(1, entitlements.TryConsumeCalls);
        Assert.Equal("ai_spend_micro_usd_per_day", entitlements.LastTryConsumeMetric);
        Assert.Equal(123, entitlements.LastTryConsumeAmount);
    }

    [Fact]
    public async Task RecordEstimatedCostAsync_RecordsDailyAndMonthlyProviderSpend()
    {
        var entitlements = new StubEntitlementService();
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), 0.02m, "provider:openrouter:chat");

        Assert.Equal(2, entitlements.TryConsumeCalls);
        Assert.Contains("ai_spend_micro_usd_per_day", entitlements.TryConsumeMetrics);
        Assert.Contains("openrouter_spend_micro_usd_per_month", entitlements.TryConsumeMetrics);
        Assert.Equal(20_000, entitlements.LastTryConsumeAmount);
    }

    [Fact]
    public async Task RecordEstimatedCostAsync_IgnoresMissingOrganizationOrCost()
    {
        var entitlements = new StubEntitlementService();
        var limiter = new AiUsageLimiter(new StubRateLimitStore(), entitlements);

        await limiter.RecordEstimatedCostAsync(null, 0.000123m, "test.operation");
        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), null, "test.operation");
        await limiter.RecordEstimatedCostAsync(Guid.NewGuid(), 0, "test.operation");

        Assert.Equal(0, entitlements.ConsumeCalls);
    }

    private sealed class StubEntitlementService : IEntitlementService
    {
        public UsageQuotaStatus TryConsumeResult { get; init; } = new(true, 1, 10);
        public UsageQuotaStatus SpendQuotaResult { get; init; } = new(true, 0, null);
        public UsageQuotaStatus ProviderSpendQuotaResult { get; init; } = new(true, 0, null);
        public int CheckQuotaCalls { get; private set; }
        public int ConsumeCalls { get; private set; }
        public int TryConsumeCalls { get; private set; }
        public string LastConsumeMetric { get; private set; } = string.Empty;
        public long LastConsumeAmount { get; private set; }
        public string LastTryConsumeMetric { get; private set; } = string.Empty;
        public long LastTryConsumeAmount { get; private set; }
        public List<string> TryConsumeMetrics { get; } = [];

        public Task<string> GetPlanKeyAsync(Guid organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult("Trial");

        public Task<bool> CanUseFeatureAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<long?> GetPlanLimitValueAsync(Guid organizationId, string featureKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(10);

        public Task<UsageQuotaStatus> CheckQuotaAsync(Guid organizationId, string metricKey, CancellationToken cancellationToken = default)
        {
            CheckQuotaCalls++;
            return Task.FromResult(metricKey.EndsWith("_per_month", StringComparison.OrdinalIgnoreCase)
                ? ProviderSpendQuotaResult
                : SpendQuotaResult);
        }

        public Task<UsageQuotaStatus> TryConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
        {
            TryConsumeCalls++;
            LastTryConsumeMetric = metricKey;
            LastTryConsumeAmount = amount;
            TryConsumeMetrics.Add(metricKey);
            return Task.FromResult(TryConsumeResult);
        }

        public Task ConsumeUsageAsync(Guid organizationId, string metricKey, long amount = 1, CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            LastConsumeMetric = metricKey;
            LastConsumeAmount = amount;
            return Task.CompletedTask;
        }
    }

    private sealed class StubRateLimitStore : IAiRateLimitStore
    {
        public UsageQuotaStatus Result { get; init; } = new(true, 1, 60);
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
            return Task.FromResult(Result);
        }
    }
}
