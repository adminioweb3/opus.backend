namespace Citationly.Application.Interfaces;

public interface IAiRateLimitStore
{
    Task<UsageQuotaStatus> TryConsumeAsync(
        string scopeKey,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        long limit,
        long amount = 1,
        CancellationToken cancellationToken = default);
}
