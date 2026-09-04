using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public sealed class AiUsageLimiter : IAiUsageLimiter
{
    public Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
