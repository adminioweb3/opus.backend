using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class VisibilitySnapshotRepository : IVisibilitySnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public VisibilitySnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static async Task EnsureSchemaAsync(System.Data.IDbConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS VisibilityScanSummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                CompositeScore INTEGER NOT NULL,
                DirectPct INTEGER NOT NULL,
                MentionsPct INTEGER NOT NULL,
                IndirectPct INTEGER NOT NULL,
                ComparativePct INTEGER NOT NULL,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_visibilityscansummaries_org_date ON VisibilityScanSummaries (OrganizationId, ScanDate);

            CREATE TABLE IF NOT EXISTS VisibilityPlatformSnapshots (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                Platform VARCHAR(255) NOT NULL,
                Score INTEGER NOT NULL,
                Citations INTEGER NOT NULL,
                Status VARCHAR(20) NOT NULL DEFAULT 'Developing',
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_visibilityplatformsnapshots_org_date ON VisibilityPlatformSnapshots (OrganizationId, ScanDate);
        ");
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var raw = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM VisibilityScanSummaries WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        DateOnly? latest = raw switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };

        return latest;
    }

    public async Task<VisibilityScanSummary?> GetLatestSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryFirstOrDefaultAsync<VisibilityScanSummary>(@"
            SELECT * FROM VisibilityScanSummaries
            WHERE OrganizationId = @OrganizationId
            ORDER BY ScanDate DESC, CreatedAt DESC
            LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<List<VisibilityScanSummary>> GetHistoryAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var rows = await connection.QueryAsync<VisibilityScanSummary>(@"
            SELECT * FROM VisibilityScanSummaries
            WHERE OrganizationId = @OrganizationId
              AND ScanDate >= CURRENT_DATE - CAST(@Days AS INTEGER) * INTERVAL '1 day'
            ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, Days = days });

        return rows.ToList();
    }

    public async Task<List<VisibilityPlatformSnapshot>> GetLatestPlatformsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var rows = await connection.QueryAsync<VisibilityPlatformSnapshot>(@"
            SELECT * FROM VisibilityPlatformSnapshots
            WHERE OrganizationId = @OrganizationId
              AND ScanDate = (
                  SELECT MAX(ScanDate) FROM VisibilityPlatformSnapshots WHERE OrganizationId = @OrganizationId
              )
            ORDER BY Platform ASC",
            new { OrganizationId = organizationId });

        return rows.ToList();
    }

    public async Task<Dictionary<string, List<int>>> GetPlatformSparklinesAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var rows = await connection.QueryAsync<(string Platform, int Score, DateOnly ScanDate)>(@"
            SELECT Platform, Score, ScanDate FROM VisibilityPlatformSnapshots
            WHERE OrganizationId = @OrganizationId
              AND ScanDate >= CURRENT_DATE - CAST(@Days AS INTEGER) * INTERVAL '1 day'
            ORDER BY Platform ASC, ScanDate ASC",
            new { OrganizationId = organizationId, Days = days });

        var result = new Dictionary<string, List<int>>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.Platform, out var list))
            {
                list = new List<int>();
                result[row.Platform] = list;
            }
            list.Add(row.Score);
        }

        foreach (var key in result.Keys.ToList())
        {
            var list = result[key];
            if (list.Count > 12)
            {
                result[key] = list.Skip(list.Count - 12).ToList();
            }
        }

        return result;
    }

    public async Task SaveSnapshotAsync(VisibilityScanSummary summary, List<VisibilityPlatformSnapshot> platforms)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        var scanDate = summary.ScanDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : summary.ScanDate;
        var now = DateTime.UtcNow;

        summary.Id = summary.Id == Guid.Empty ? Guid.NewGuid() : summary.Id;
        summary.ScanDate = scanDate;
        summary.CreatedAt = now;

        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO VisibilityScanSummaries (Id, OrganizationId, ScanDate, CompositeScore, DirectPct, MentionsPct, IndirectPct, ComparativePct, CreatedAt)
                VALUES (@Id, @OrganizationId, @ScanDate, @CompositeScore, @DirectPct, @MentionsPct, @IndirectPct, @ComparativePct, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var platform in platforms)
            {
                platform.Id = platform.Id == Guid.Empty ? Guid.NewGuid() : platform.Id;
                platform.OrganizationId = summary.OrganizationId;
                platform.ScanDate = scanDate;
                platform.CreatedAt = now;

                await connection.ExecuteAsync(@"
                    INSERT INTO VisibilityPlatformSnapshots (Id, OrganizationId, ScanDate, Platform, Score, Citations, Status, CreatedAt)
                    VALUES (@Id, @OrganizationId, @ScanDate, @Platform, @Score, @Citations, @Status, @CreatedAt)",
                    platform, transaction: transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
