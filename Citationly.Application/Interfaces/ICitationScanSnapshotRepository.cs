using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ICitationScanSnapshotRepository
{
    /// <summary>
    /// Returns the most recent ScanDate recorded for this organization, or null if no scans exist yet.
    /// </summary>
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);

    /// <summary>
    /// Returns the CitationScanSummary row for the most recent ScanDate for this organization, or null if none exists.
    /// </summary>
    Task<CitationScanSummary?> GetLatestSummaryAsync(Guid organizationId);

    /// <summary>
    /// Returns the CitationScanSummary row for the second-most-recent ScanDate (the scan immediately
    /// preceding the latest one), used to compute deltas. Null if there is no prior scan.
    /// </summary>
    Task<CitationScanSummary?> GetPreviousSummaryAsync(Guid organizationId);

    /// <summary>
    /// Returns all CitationScanSummary rows for this organization within the last `days` days,
    /// ordered by ScanDate ascending (oldest first) — suitable for building a trend/history chart.
    /// </summary>
    Task<IEnumerable<CitationScanSummary>> GetHistoryAsync(Guid organizationId, int days);

    /// <summary>
    /// Returns all CitationSourceSnapshot rows recorded on the organization's latest ScanDate.
    /// </summary>
    Task<IEnumerable<CitationSourceSnapshot>> GetLatestSourcesAsync(Guid organizationId);

    /// <summary>
    /// Persists a new scan: one CitationScanSummary row plus its associated CitationSourceSnapshot rows.
    /// </summary>
    Task SaveSnapshotAsync(CitationScanSummary summary, List<CitationSourceSnapshot> sources);
}
