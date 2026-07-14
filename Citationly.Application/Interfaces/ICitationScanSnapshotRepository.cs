using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ICitationScanSnapshotRepository
{
    Task EnsureTableCreatedAsync();
    Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task InsertSummaryAsync(CitationScanSummary summary);
    Task InsertSourceSnapshotAsync(CitationSourceSnapshot snapshot);
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);
    Task<CitationScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate);
    Task<List<CitationSourceSnapshot>> GetSourceSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate);

    /// <summary>Summary rows across the most recent <paramref name="maxScanDates"/> distinct scan dates, for trend charts.</summary>
    Task<List<CitationScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13);
}
