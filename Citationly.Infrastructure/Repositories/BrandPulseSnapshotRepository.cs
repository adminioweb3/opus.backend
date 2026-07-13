using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class BrandPulseSnapshotRepository : IBrandPulseSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public BrandPulseSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static async Task EnsureSchemaAsync(System.Data.IDbConnection connection)
    {
        // Self-healing schema — matches the existing live table exactly (safe/idempotent against the dev DB).
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS BrandPulseScanSummaries (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                BrandHealth INT NOT NULL,
                AiConfidence INT NOT NULL,
                MessagingConsistency INT NOT NULL,
                BrandTrust INT NOT NULL,
                SentimentPositive INT NOT NULL,
                SentimentNeutral INT NOT NULL,
                SentimentNegative INT NOT NULL,
                AlertsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                ModelInsightsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                AccuracyFlagsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                PromptEvidenceJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_brandpulsescansummaries_org_scandate
                ON BrandPulseScanSummaries (OrganizationId, ScanDate);
        ");

        // Self-heal the additional SharePerceptionJson column that isn't part of the original live schema yet.
        await connection.ExecuteAsync(@"
            DO $$
            BEGIN
                BEGIN
                    ALTER TABLE BrandPulseScanSummaries ADD COLUMN SharePerceptionJson JSONB NOT NULL DEFAULT '[]'::jsonb;
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
            "SELECT MAX(ScanDate) FROM BrandPulseScanSummaries WHERE OrganizationId = @OrganizationId",
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

    public async Task<BrandPulseScanSummary?> GetLatestSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryFirstOrDefaultAsync<BrandPulseScanSummary>(@"
            SELECT * FROM BrandPulseScanSummaries
            WHERE OrganizationId = @OrganizationId
            ORDER BY ScanDate DESC, CreatedAt DESC
            LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<BrandPulseScanSummary?> GetPreviousSummaryAsync(Guid organizationId, DateOnly beforeDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryFirstOrDefaultAsync<BrandPulseScanSummary>(@"
            SELECT * FROM BrandPulseScanSummaries
            WHERE OrganizationId = @OrganizationId AND ScanDate < @BeforeDate
            ORDER BY ScanDate DESC, CreatedAt DESC
            LIMIT 1",
            new { OrganizationId = organizationId, BeforeDate = beforeDate });
    }

    public async Task<IEnumerable<BrandPulseScanSummary>> GetHistoryAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        return await connection.QueryAsync<BrandPulseScanSummary>(@"
            SELECT * FROM BrandPulseScanSummaries
            WHERE OrganizationId = @OrganizationId
              AND ScanDate >= CURRENT_DATE - make_interval(days => @Days)
            ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, Days = days });
    }

    public async Task SaveSnapshotAsync(BrandPulseScanSummary summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        if (summary.Id == Guid.Empty)
        {
            summary.Id = Guid.NewGuid();
        }
        if (summary.CreatedAt == default)
        {
            summary.CreatedAt = DateTime.UtcNow;
        }

        await connection.ExecuteAsync(@"
            INSERT INTO BrandPulseScanSummaries
                (Id, OrganizationId, ScanDate, BrandHealth, AiConfidence, MessagingConsistency, BrandTrust,
                 SentimentPositive, SentimentNeutral, SentimentNegative,
                 AlertsJson, ModelInsightsJson, AccuracyFlagsJson, PromptEvidenceJson, SharePerceptionJson, CreatedAt)
            VALUES
                (@Id, @OrganizationId, @ScanDate, @BrandHealth, @AiConfidence, @MessagingConsistency, @BrandTrust,
                 @SentimentPositive, @SentimentNeutral, @SentimentNegative,
                 @AlertsJson::jsonb, @ModelInsightsJson::jsonb, @AccuracyFlagsJson::jsonb, @PromptEvidenceJson::jsonb, @SharePerceptionJson::jsonb, @CreatedAt)",
            new
            {
                summary.Id,
                summary.OrganizationId,
                summary.ScanDate,
                summary.BrandHealth,
                summary.AiConfidence,
                summary.MessagingConsistency,
                summary.BrandTrust,
                summary.SentimentPositive,
                summary.SentimentNeutral,
                summary.SentimentNegative,
                summary.AlertsJson,
                summary.ModelInsightsJson,
                summary.AccuracyFlagsJson,
                summary.PromptEvidenceJson,
                summary.SharePerceptionJson,
                summary.CreatedAt
            });
    }
}
