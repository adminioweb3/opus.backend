using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IOpportunitySnapshotRepository
{
    Task EnsureTableCreatedAsync();
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task InsertAsync(OpportunitySnapshot snapshot);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<List<OpportunitySnapshot>> GetSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate);

    /// <summary>Snapshots across the most recent <paramref name="maxScanDates"/> distinct scan dates, for real forecast trend.</summary>
    Task<List<OpportunitySnapshot>> GetRecentHistoryAsync(Guid organizationId, int maxScanDates = 13);
}
