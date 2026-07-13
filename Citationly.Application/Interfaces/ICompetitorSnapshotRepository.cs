using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

/// <summary>
/// Repository for CompetitorSnapshots — one row per scan per tracked entity (the organization itself,
/// IsYou = true, plus one row per tracked competitor, IsYou = false), all sharing the same ScanDate per scan.
/// </summary>
public interface ICompetitorSnapshotRepository
{
    /// <summary>
    /// Returns the most recent ScanDate recorded for this organization, or null if no scan has ever run.
    /// </summary>
    Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId);

    /// <summary>
    /// Returns every row (the org itself + all competitors) for the most recent ScanDate, ordered by Rank ascending.
    /// Returns an empty sequence if no scan has ever run.
    /// </summary>
    Task<IEnumerable<CompetitorSnapshot>> GetLatestSnapshotsAsync(Guid organizationId);

    /// <summary>
    /// Returns the Visibility values for one specific tracked entity across scans within the last <paramref name="days"/> days,
    /// ordered by ScanDate ascending, capped to the most recent 12 points.
    /// When <paramref name="isYou"/> is true, matches the row where IsYou = true (CompetitorId is null for that row).
    /// When <paramref name="isYou"/> is false, matches rows where CompetitorId = <paramref name="competitorId"/>.
    /// </summary>
    Task<List<int>> GetTrendAsync(Guid organizationId, Guid? competitorId, bool isYou, int days);

    /// <summary>
    /// Persists a brand-new scan: the "you" row plus every competitor row, all stamped with today's ScanDate.
    /// </summary>
    Task SaveScanAsync(Guid organizationId, CompetitorSnapshot you, List<CompetitorSnapshot> competitors);
}
