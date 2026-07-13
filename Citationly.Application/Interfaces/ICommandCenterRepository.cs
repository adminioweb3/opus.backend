namespace Citationly.Application.Interfaces;

/// <summary>
/// Current value + short history + previous-period value for a single KPI metric
/// sourced from a sibling feature's scan summary table.
/// </summary>
public class CommandCenterMetric
{
    public double Current { get; set; }
    public double Previous { get; set; }
    public List<double> History { get; set; } = new();
    public bool HasData { get; set; }
    public DateOnly? LatestScanDate { get; set; }
}

/// <summary>
/// Bundle of the 4 sibling-feature metrics that the Command Center KPI row is built from.
/// </summary>
public class CommandCenterSiblingData
{
    public CommandCenterMetric Visibility { get; set; } = new();
    public CommandCenterMetric CitationQuality { get; set; } = new();
    public CommandCenterMetric BrandHealth { get; set; } = new();
    public CommandCenterMetric ShareOfVoice { get; set; } = new();

    /// <summary>Raw AccuracyFlagsJson/AlertsJson text from the latest brandpulsescansummaries row, if any.</summary>
    public string? BrandPulseAccuracyFlagsJson { get; set; }
    public string? BrandPulseAlertsJson { get; set; }

    public bool AnyDataAvailable =>
        Visibility.HasData || CitationQuality.HasData || BrandHealth.HasData || ShareOfVoice.HasData;
}

public interface ICommandCenterRepository
{
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);

    Task<List<string>> GetLatestInsightsAsync(Guid organizationId);

    Task SaveInsightsAsync(Guid organizationId, DateOnly scanDate, List<string> insights);

    /// <summary>
    /// Current/previous values always reflect the two most recent scans regardless of range.
    /// The History array is bounded to the last <paramref name="rangeDays"/> days (for trend/sparkline display).
    /// </summary>
    Task<CommandCenterSiblingData> GetSiblingSnapshotsAsync(Guid organizationId, int rangeDays);
}
