using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IBrandPulseSnapshotRepository
{
    Task EnsureTableCreatedAsync();
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task InsertSummaryAsync(BrandPulseScanSummary summary);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<BrandPulseScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate);

    /// <summary>Summary rows across the most recent <paramref name="maxScanDates"/> distinct scan dates, for trend charts.</summary>
    Task<List<BrandPulseScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13);
}
