using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IVisibilitySnapshotRepository
{
    Task EnsureTableCreatedAsync();
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task InsertSummaryAsync(VisibilityScanSummary summary);
    Task InsertPlatformSnapshotAsync(VisibilityPlatformSnapshot snapshot);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<VisibilityScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task<List<VisibilityPlatformSnapshot>> GetPlatformSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate);

    /// <summary>Summary rows across the most recent <paramref name="maxScanDates"/> distinct scan dates, for the composite score history chart.</summary>
    Task<List<VisibilityScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13);

    /// <summary>Platform rows across the most recent <paramref name="maxScanDates"/> distinct scan dates, for per-platform sparklines.</summary>
    Task<List<VisibilityPlatformSnapshot>> GetRecentPlatformHistoryAsync(Guid organizationId, int maxScanDates = 13);
}
