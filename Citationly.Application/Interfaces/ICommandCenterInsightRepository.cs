using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ICommandCenterInsightRepository
{
    Task EnsureTableCreatedAsync();
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task InsertAsync(CommandCenterInsightSnapshot snapshot);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<CommandCenterInsightSnapshot?> GetLatestAsync(Guid organizationId);
}
