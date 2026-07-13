using System.Data;
using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class CompetitorSnapshotRepository : ICompetitorSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CompetitorSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    /// <summary>
    /// Self-healing schema bootstrap. Matches the EXACT existing live "competitorsnapshots" table,
    /// plus the 3 additional columns (CitationsShare, CitationsTotal, ContentVelocity) the entity now needs
    /// but that don't exist as real columns yet.
    /// </summary>
    private static async Task EnsureSchemaAsync(IDbConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CompetitorSnapshots (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                CompetitorId UUID NULL,
                IsYou BOOLEAN NOT NULL DEFAULT FALSE,
                ScanDate DATE NOT NULL,
                Name VARCHAR(255) NOT NULL,
                Score INT NOT NULL DEFAULT 0,
                Rank INT NOT NULL DEFAULT 0,
                ShareOfVoice INT NOT NULL DEFAULT 0,
                ShareOfVoiceChange INT NOT NULL DEFAULT 0,
                Visibility INT NOT NULL DEFAULT 0,
                VisibilityChange INT NOT NULL DEFAULT 0,
                Threat VARCHAR(10) NOT NULL DEFAULT 'low',
                ModelsJson JSONB NOT NULL DEFAULT '{}'::jsonb,
                Tagline VARCHAR(512) NULL,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                WebsiteUrl VARCHAR(500) NULL
            );
            CREATE INDEX IF NOT EXISTS idx_competitorsnapshots_org_scandate ON CompetitorSnapshots (OrganizationId, ScanDate);
        ");

        await connection.ExecuteAsync(@"
            DO $$
            BEGIN
                BEGIN
                    ALTER TABLE CompetitorSnapshots ADD COLUMN CitationsShare INT NOT NULL DEFAULT 0;
                EXCEPTION WHEN duplicate_column THEN null;
                END;
                BEGIN
                    ALTER TABLE CompetitorSnapshots ADD COLUMN CitationsTotal INT NOT NULL DEFAULT 0;
                EXCEPTION WHEN duplicate_column THEN null;
                END;
                BEGIN
                    ALTER TABLE CompetitorSnapshots ADD COLUMN ContentVelocity VARCHAR(100) NOT NULL DEFAULT '';
                EXCEPTION WHEN duplicate_column THEN null;
                END;
            END $$;
        ");
    }

    private static async Task<DateOnly?> ReadLatestScanDateAsync(IDbConnection connection, Guid organizationId)
    {
        var raw = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return raw switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);
        return await ReadLatestScanDateAsync(connection, organizationId);
    }

    public async Task<IEnumerable<CompetitorSnapshot>> GetLatestSnapshotsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var latestDate = await ReadLatestScanDateAsync(connection, organizationId);
        if (latestDate == null)
        {
            return Enumerable.Empty<CompetitorSnapshot>();
        }

        return await connection.QueryAsync<CompetitorSnapshot>(
            "SELECT * FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate ORDER BY Rank ASC",
            new { OrganizationId = organizationId, ScanDate = latestDate.Value });
    }

    public async Task<List<int>> GetTrendAsync(Guid organizationId, Guid? competitorId, bool isYou, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        // `days` is a server-derived int (parsed from the validated "7d"/"30d"/"90d" range enum), safe to interpolate.
        IEnumerable<int> values;
        if (isYou)
        {
            values = await connection.QueryAsync<int>($@"
                SELECT Visibility FROM CompetitorSnapshots
                WHERE OrganizationId = @OrganizationId
                  AND IsYou = TRUE
                  AND ScanDate >= CURRENT_DATE - INTERVAL '{days} days'
                ORDER BY ScanDate ASC",
                new { OrganizationId = organizationId });
        }
        else
        {
            values = await connection.QueryAsync<int>($@"
                SELECT Visibility FROM CompetitorSnapshots
                WHERE OrganizationId = @OrganizationId
                  AND IsYou = FALSE
                  AND CompetitorId = @CompetitorId
                  AND ScanDate >= CURRENT_DATE - INTERVAL '{days} days'
                ORDER BY ScanDate ASC",
                new { OrganizationId = organizationId, CompetitorId = competitorId });
        }

        var list = values.ToList();
        return list.Count > 12 ? list.Skip(list.Count - 12).ToList() : list;
    }

    public async Task SaveScanAsync(Guid organizationId, CompetitorSnapshot you, List<CompetitorSnapshot> competitors)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        var scanDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var allRows = new List<CompetitorSnapshot> { you };
        allRows.AddRange(competitors);

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var row in allRows)
            {
                if (row.Id == Guid.Empty)
                {
                    row.Id = Guid.NewGuid();
                }
                row.OrganizationId = organizationId;
                row.ScanDate = scanDate;
                row.CreatedAt = DateTime.UtcNow;

                await connection.ExecuteAsync(@"
                    INSERT INTO CompetitorSnapshots
                        (Id, OrganizationId, CompetitorId, IsYou, ScanDate, Name, Score, Rank, ShareOfVoice, ShareOfVoiceChange,
                         Visibility, VisibilityChange, Threat, ModelsJson, Tagline, CreatedAt, WebsiteUrl,
                         CitationsShare, CitationsTotal, ContentVelocity)
                    VALUES
                        (@Id, @OrganizationId, @CompetitorId, @IsYou, @ScanDate, @Name, @Score, @Rank, @ShareOfVoice, @ShareOfVoiceChange,
                         @Visibility, @VisibilityChange, @Threat, @ModelsJson::jsonb, @Tagline, @CreatedAt, @WebsiteUrl,
                         @CitationsShare, @CitationsTotal, @ContentVelocity)",
                    new
                    {
                        row.Id,
                        row.OrganizationId,
                        CompetitorId = row.CompetitorId.HasValue ? (object)row.CompetitorId.Value : DBNull.Value,
                        row.IsYou,
                        row.ScanDate,
                        row.Name,
                        row.Score,
                        row.Rank,
                        row.ShareOfVoice,
                        row.ShareOfVoiceChange,
                        row.Visibility,
                        row.VisibilityChange,
                        row.Threat,
                        row.ModelsJson,
                        Tagline = (object?)row.Tagline ?? DBNull.Value,
                        row.CreatedAt,
                        WebsiteUrl = (object?)row.WebsiteUrl ?? DBNull.Value,
                        row.CitationsShare,
                        row.CitationsTotal,
                        row.ContentVelocity
                    }, transaction: transaction);
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
