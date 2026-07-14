using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class CompetitorSnapshotRepository : ICompetitorSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CompetitorSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task EnsureTableCreatedAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CompetitorSnapshots (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                CompetitorId UUID,
                IsYou BOOLEAN NOT NULL DEFAULT false,
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
                Tagline VARCHAR(512),
                WebsiteUrl VARCHAR(500),
                CreatedAt TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_competitorsnapshots_org_scandate ON CompetitorSnapshots (OrganizationId, ScanDate);
            ALTER TABLE CompetitorSnapshots ADD COLUMN IF NOT EXISTS WebsiteUrl VARCHAR(500);
        ");
    }

    public async Task<Guid> InsertSnapshotAsync(CompetitorSnapshot snapshot)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO CompetitorSnapshots
                (OrganizationId, CompetitorId, IsYou, ScanDate, Name, Score, Rank, ShareOfVoice, ShareOfVoiceChange, Visibility, VisibilityChange, Threat, ModelsJson, Tagline, WebsiteUrl)
              VALUES
                (@OrganizationId, @CompetitorId, @IsYou, @ScanDate, @Name, @Score, @Rank, @ShareOfVoice, @ShareOfVoiceChange, @Visibility, @VisibilityChange, @Threat, @ModelsJson::jsonb, @Tagline, @WebsiteUrl)
              RETURNING Id",
            snapshot);
    }

    public async Task DeleteByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // Raw ExecuteScalarAsync (not a typed Query) to sidestep Dapper's Nullable<T> conversion
        // path entirely: Npgsql returns a native DateOnly for non-null "date" columns (which the
        // shared DateOnlyTypeHandler/Convert.ChangeType can't reconcile with DBNull for MAX()
        // over zero rows), so we just pattern-match whatever comes back.
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<List<CompetitorSnapshot>> GetSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<CompetitorSnapshot>(
            "SELECT * FROM CompetitorSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate ORDER BY Rank",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        return results.ToList();
    }

    public async Task<List<CompetitorSnapshot>> GetRecentHistoryAsync(Guid organizationId, int maxScanDates = 12)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<CompetitorSnapshot>(
            @"SELECT * FROM CompetitorSnapshots
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM CompetitorSnapshots
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }
}
