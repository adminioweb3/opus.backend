using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class CitationScanSnapshotRepository : ICitationScanSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CitationScanSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static async Task EnsureSchemaAsync(System.Data.IDbConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CitationScanSummaries (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                CompositeQualityScore INT NOT NULL,
                AverageAuthorityScore INT NOT NULL,
                AverageInfluenceScore INT NOT NULL,
                CitationSignal INT NOT NULL,
                ModelsReferencingCount INT NOT NULL,
                ModelsTrackedCount INT NOT NULL,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_citationscansummaries_org_date ON CitationScanSummaries (OrganizationId, ScanDate);

            CREATE TABLE IF NOT EXISTS CitationSourceSnapshots (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                Source VARCHAR(255) NOT NULL,
                Category VARCHAR(255) NULL,
                AuthorityScore INT NOT NULL,
                InfluenceScore INT NOT NULL,
                CitationFrequency INT NOT NULL,
                CompetitorCoverage INT NOT NULL,
                OpportunityScore INT NOT NULL,
                MentionProbability INT NOT NULL,
                Reason TEXT NULL,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_citationsourcesnapshots_org_date ON CitationSourceSnapshots (OrganizationId, ScanDate);
        ");

        await connection.ExecuteAsync(@"
            DO $$
            BEGIN
                BEGIN
                    ALTER TABLE CitationScanSummaries ADD COLUMN PlatformsJson JSONB NOT NULL DEFAULT '[]'::jsonb;
                EXCEPTION WHEN duplicate_column THEN null;
                END;
            END $$;
        ");
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var raw = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CitationScanSummaries WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return raw switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<CitationScanSummary?> GetLatestSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryFirstOrDefaultAsync<CitationScanSummary>(@"
            SELECT * FROM CitationScanSummaries
            WHERE OrganizationId = @OrganizationId
            ORDER BY ScanDate DESC, CreatedAt DESC
            LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<CitationScanSummary?> GetPreviousSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryFirstOrDefaultAsync<CitationScanSummary>(@"
            SELECT * FROM CitationScanSummaries
            WHERE OrganizationId = @OrganizationId
            ORDER BY ScanDate DESC, CreatedAt DESC
            OFFSET 1 LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<CitationScanSummary>> GetHistoryAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryAsync<CitationScanSummary>(@"
            SELECT * FROM CitationScanSummaries
            WHERE OrganizationId = @OrganizationId
              AND ScanDate >= CURRENT_DATE - (@Days || ' days')::interval
            ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, Days = days });
    }

    public async Task<IEnumerable<CitationSourceSnapshot>> GetLatestSourcesAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var latestDate = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CitationSourceSnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        DateOnly? scanDate = latestDate switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };

        if (scanDate == null)
        {
            return Enumerable.Empty<CitationSourceSnapshot>();
        }

        return await connection.QueryAsync<CitationSourceSnapshot>(@"
            SELECT * FROM CitationSourceSnapshots
            WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate.Value });
    }

    public async Task SaveSnapshotAsync(CitationScanSummary summary, List<CitationSourceSnapshot> sources)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            if (summary.Id == Guid.Empty)
            {
                summary.Id = Guid.NewGuid();
            }
            if (summary.CreatedAt == default)
            {
                summary.CreatedAt = DateTime.UtcNow;
            }

            await connection.ExecuteAsync(@"
                INSERT INTO CitationScanSummaries
                    (Id, OrganizationId, ScanDate, CompositeQualityScore, AverageAuthorityScore, AverageInfluenceScore,
                     CitationSignal, ModelsReferencingCount, ModelsTrackedCount, PlatformsJson, CreatedAt)
                VALUES
                    (@Id, @OrganizationId, @ScanDate, @CompositeQualityScore, @AverageAuthorityScore, @AverageInfluenceScore,
                     @CitationSignal, @ModelsReferencingCount, @ModelsTrackedCount, @PlatformsJson::jsonb, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var source in sources)
            {
                if (source.Id == Guid.Empty)
                {
                    source.Id = Guid.NewGuid();
                }
                if (source.CreatedAt == default)
                {
                    source.CreatedAt = DateTime.UtcNow;
                }
                source.OrganizationId = summary.OrganizationId;
                source.ScanDate = summary.ScanDate;

                await connection.ExecuteAsync(@"
                    INSERT INTO CitationSourceSnapshots
                        (Id, OrganizationId, ScanDate, Source, Category, AuthorityScore, InfluenceScore,
                         CitationFrequency, CompetitorCoverage, OpportunityScore, MentionProbability, Reason, CreatedAt)
                    VALUES
                        (@Id, @OrganizationId, @ScanDate, @Source, @Category, @AuthorityScore, @InfluenceScore,
                         @CitationFrequency, @CompetitorCoverage, @OpportunityScore, @MentionProbability, @Reason, @CreatedAt)",
                    source, transaction: transaction);
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
