namespace Citationly.Application.Interfaces;

public interface IAiUsageLimiter
{
    Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default);
    Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default);
}
