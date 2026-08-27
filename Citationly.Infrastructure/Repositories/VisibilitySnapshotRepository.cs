using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class VisibilitySnapshotRepository : IVisibilitySnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public VisibilitySnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public Task EnsureTableCreatedAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM VisibilityScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        await connection.ExecuteAsync(
            "DELETE FROM VisibilityPlatformSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task InsertSummaryAsync(VisibilityScanSummary summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO VisibilityScanSummaries
                (OrganizationId, ScanDate, CompositeScore, DirectPct, MentionsPct, IndirectPct, ComparativePct)
              VALUES
                (@OrganizationId, @ScanDate, @CompositeScore, @DirectPct, @MentionsPct, @IndirectPct, @ComparativePct)",
            summary);
    }

    public async Task InsertPlatformSnapshotAsync(VisibilityPlatformSnapshot snapshot)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO VisibilityPlatformSnapshots
                (OrganizationId, ScanDate, Platform, Score, Citations, Status)
              VALUES
                (@OrganizationId, @ScanDate, @Platform, @Score, @Citations, @Status)",
            snapshot);
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // Raw ExecuteScalarAsync (not a typed Query) to sidestep Dapper's Nullable<T> conversion
        // path: Npgsql returns a native DateOnly for non-null "date" columns, which the shared
        // DateOnlyTypeHandler/Convert.ChangeType can't reconcile with DBNull for MAX() over zero rows.
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM VisibilityScanSummaries WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<VisibilityScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<VisibilityScanSummary>(
            "SELECT * FROM VisibilityScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task<List<VisibilityPlatformSnapshot>> GetPlatformSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<VisibilityPlatformSnapshot>(
            "SELECT * FROM VisibilityPlatformSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate ORDER BY Score DESC",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        return results.ToList();
    }

    public async Task<List<VisibilityScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<VisibilityScanSummary>(
            @"SELECT * FROM VisibilityScanSummaries
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM VisibilityScanSummaries
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }

    public async Task<List<VisibilityPlatformSnapshot>> GetRecentPlatformHistoryAsync(Guid organizationId, int maxScanDates = 13)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<VisibilityPlatformSnapshot>(
            @"SELECT * FROM VisibilityPlatformSnapshots
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM VisibilityPlatformSnapshots
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }
}
