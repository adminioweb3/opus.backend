using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IBrandPulseSnapshotRepository
{
    /// <summary>
    /// Returns the ScanDate of the most recent brand pulse scan for the organization, or null if none exist.
    /// </summary>
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);

    /// <summary>
    /// Returns the most recent brand pulse scan summary row for the organization, or null if none exist.
    /// </summary>
    Task<BrandPulseScanSummary?> GetLatestSummaryAsync(Guid organizationId);

    /// <summary>
    /// Returns the most recent brand pulse scan summary row strictly before the given date (used for delta calculations).
    /// </summary>
    Task<BrandPulseScanSummary?> GetPreviousSummaryAsync(Guid organizationId, DateOnly beforeDate);

    /// <summary>
    /// Returns all scan summary rows within the last `days` days, ordered oldest -> newest,
    /// for building the parallel metricHistory arrays.
    /// </summary>
    Task<IEnumerable<BrandPulseScanSummary>> GetHistoryAsync(Guid organizationId, int days);

    /// <summary>
    /// Persists a new brand pulse scan summary snapshot.
    /// </summary>
    Task SaveSnapshotAsync(BrandPulseScanSummary summary);
}
