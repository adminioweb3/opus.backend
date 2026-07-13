using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IOpportunitySnapshotRepository
{
    /// <summary>
    /// Returns the most recent ScanDate recorded for this organization, or null if no scan has ever run.
    /// </summary>
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);

    /// <summary>
    /// Returns all opportunity rows belonging to the most recent scan for this organization.
    /// Empty if no scan has ever run.
    /// </summary>
    Task<IEnumerable<OpportunitySnapshot>> GetLatestOpportunitiesAsync(Guid organizationId);

    /// <summary>
    /// Returns one aggregate row per distinct ScanDate within the last `days` days:
    /// average Score across that day's opportunities and the count of opportunities that day.
    /// Ordered ascending by date (oldest first) for charting.
    /// </summary>
    Task<IEnumerable<OpportunityDailyAggregate>> GetHistoricalAggregatesAsync(Guid organizationId, int days);

    /// <summary>
    /// Total number of opportunity rows ever recorded for this organization (used to detect the
    /// "zero data yet" bootstrap condition).
    /// </summary>
    Task<int> GetOpportunityCountAsync(Guid organizationId);

    /// <summary>
    /// Persists a brand-new scan: every opportunity gets a new Id, a shared ScanDate of "today",
    /// and a unique short OpportunityKey. Returns the persisted rows (with Id/ScanDate/OpportunityKey populated).
    /// </summary>
    Task<List<OpportunitySnapshot>> SaveScanAsync(Guid organizationId, List<OpportunitySnapshot> opportunities);
}

public class OpportunityDailyAggregate
{
    public DateOnly ScanDate { get; set; }
    public double AvgScore { get; set; }
    public int Count { get; set; }
}
