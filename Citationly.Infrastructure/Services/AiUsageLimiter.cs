using System.Collections.Concurrent;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    private const string DailyQuotaMetricKey = "ai_calls_per_day";

    // Fast, in-process burst guard - catches a runaway loop within the hour regardless of
    // plan. This is defense in depth, not the real limit: the actual per-tenant contractual
    // cap is the persisted, plan-aware "ai_calls_per_day" quota enforced below via
    // IEntitlementService, which (unlike this in-memory window) survives an app restart and
    // is shared across every instance of the API.
    private static readonly TimeSpan WindowSize = TimeSpan.FromHours(1);
    private const int TenantLimitPerWindow = 80;
    private const int GlobalLimitPerWindow = 500;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, UsageWindow> _windows = new();
    private readonly IEntitlementService _entitlements;

    public AiUsageLimiter(IEntitlementService entitlements)
    {
        _entitlements = entitlements;
    }

    public async Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default)
    {
        var tenantKey = $"tenant:{organizationId?.ToString() ?? "anonymous"}";
        await EnsureWithinLimitAsync(tenantKey, TenantLimitPerWindow, operationName, cancellationToken);
        await EnsureWithinLimitAsync("global", GlobalLimitPerWindow, operationName, cancellationToken);

        if (organizationId is Guid orgId)
        {
            var quota = await _entitlements.CheckQuotaAsync(orgId, DailyQuotaMetricKey, cancellationToken);
            if (!quota.IsWithinLimit)
            {
                throw new InvalidOperationException(
                    $"Daily AI usage limit reached for {operationName} ({quota.CurrentUsage}/{quota.Limit}). " +
                    "Upgrade your plan or try again tomorrow.");
            }

            await _entitlements.ConsumeUsageAsync(orgId, DailyQuotaMetricKey, cancellationToken: cancellationToken);
        }
    }

    private async Task EnsureWithinLimitAsync(string key, int limit, string operationName, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var window = _windows.GetOrAdd(key, _ => new UsageWindow(now, 0));

            if (now - window.WindowStartUtc >= WindowSize)
            {
                window = new UsageWindow(now, 0);
            }

            if (window.Count >= limit)
            {
                throw new InvalidOperationException($"AI usage limit exceeded for {operationName}. Try again later.");
            }

            _windows[key] = window with { Count = window.Count + 1 };
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record UsageWindow(DateTimeOffset WindowStartUtc, int Count);
}
