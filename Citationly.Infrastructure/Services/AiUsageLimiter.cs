using System.Collections.Concurrent;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    private static readonly TimeSpan WindowSize = TimeSpan.FromHours(1);
    private const int TenantLimitPerWindow = 80;
    private const int GlobalLimitPerWindow = 500;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, UsageWindow> _windows = new();

    public async Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default)
    {
        var tenantKey = $"tenant:{organizationId?.ToString() ?? "anonymous"}";
        await EnsureWithinLimitAsync(tenantKey, TenantLimitPerWindow, operationName, cancellationToken);
        await EnsureWithinLimitAsync("global", GlobalLimitPerWindow, operationName, cancellationToken);
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
