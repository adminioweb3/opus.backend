using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ICompetitorSnapshotRepository
{
    Task EnsureTableCreatedAsync();
    Task<Guid> InsertSnapshotAsync(CompetitorSnapshot snapshot);
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<List<CompetitorSnapshot>> GetSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate);

    /// <summary>Snapshots across the most recent <paramref name="maxScanDates"/> distinct scan dates, for building trend history.</summary>
    Task<List<CompetitorSnapshot>> GetRecentHistoryAsync(Guid organizationId, int maxScanDates = 12);
}
