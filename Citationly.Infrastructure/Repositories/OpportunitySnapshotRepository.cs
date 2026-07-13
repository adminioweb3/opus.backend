using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class OpportunitySnapshotRepository : IOpportunitySnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public OpportunitySnapshotRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static async Task EnsureTableAsync(System.Data.IDbConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS OpportunitySnapshots (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                OpportunityKey VARCHAR(20) NOT NULL,
                Category VARCHAR(100) NOT NULL,
                Title VARCHAR(255) NOT NULL,
                Summary TEXT NULL,
                WhyItMatters TEXT NULL,
                Score INT NOT NULL,
                Effort INT NOT NULL,
                EstimatedGainPct DOUBLE PRECISION NOT NULL,
                Eta VARCHAR(100) NULL,
                CompetitorContext TEXT NULL,
                ChecklistJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ix_opportunitysnapshots_org_scandate
                ON OpportunitySnapshots (OrganizationId, ScanDate);
        ");
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureTableAsync(connection);

        var raw = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId",
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

    public async Task<IEnumerable<OpportunitySnapshot>> GetLatestOpportunitiesAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureTableAsync(connection);

        return await connection.QueryAsync<OpportunitySnapshot>(@"
            SELECT * FROM OpportunitySnapshots
            WHERE OrganizationId = @OrganizationId
              AND ScanDate = (SELECT MAX(ScanDate) FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId)
            ORDER BY Score DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<OpportunityDailyAggregate>> GetHistoricalAggregatesAsync(Guid organizationId, int days)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureTableAsync(connection);

        var rows = await connection.QueryAsync<OpportunityDailyAggregateRow>(@"
            SELECT ScanDate AS ScanDate, AVG(Score)::float8 AS AvgScore, COUNT(*)::int AS Count
            FROM OpportunitySnapshots
            WHERE OrganizationId = @OrganizationId
              AND ScanDate >= CURRENT_DATE - (@Days || ' days')::interval
            GROUP BY ScanDate
            ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, Days = days });

        return rows.Select(r => new OpportunityDailyAggregate
        {
            ScanDate = r.ScanDate,
            AvgScore = r.AvgScore,
            Count = r.Count
        });
    }

    public async Task<int> GetOpportunityCountAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureTableAsync(connection);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task<List<OpportunitySnapshot>> SaveScanAsync(Guid organizationId, List<OpportunitySnapshot> opportunities)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureTableAsync(connection);

        var scanDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            var index = 1;
            foreach (var opp in opportunities)
            {
                opp.Id = Guid.NewGuid();
                opp.OrganizationId = organizationId;
                opp.ScanDate = scanDate;
                opp.OpportunityKey = $"opp-{index:D2}";
                opp.CreatedAt = now;
                index++;

                await connection.ExecuteAsync(@"
                    INSERT INTO OpportunitySnapshots
                        (Id, OrganizationId, ScanDate, OpportunityKey, Category, Title, Summary, WhyItMatters,
                         Score, Effort, EstimatedGainPct, Eta, CompetitorContext, ChecklistJson, CreatedAt)
                    VALUES
                        (@Id, @OrganizationId, @ScanDate, @OpportunityKey, @Category, @Title, @Summary, @WhyItMatters,
                         @Score, @Effort, @EstimatedGainPct, @Eta, @CompetitorContext, @ChecklistJson::jsonb, @CreatedAt)",
                    new
                    {
                        opp.Id,
                        opp.OrganizationId,
                        opp.ScanDate,
                        opp.OpportunityKey,
                        opp.Category,
                        opp.Title,
                        Summary = (object?)opp.Summary ?? DBNull.Value,
                        WhyItMatters = (object?)opp.WhyItMatters ?? DBNull.Value,
                        opp.Score,
                        opp.Effort,
                        opp.EstimatedGainPct,
                        Eta = (object?)opp.Eta ?? DBNull.Value,
                        CompetitorContext = (object?)opp.CompetitorContext ?? DBNull.Value,
                        ChecklistJson = string.IsNullOrWhiteSpace(opp.ChecklistJson) ? "[]" : opp.ChecklistJson,
                        opp.CreatedAt
                    },
                    transaction: transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return opportunities;
    }

    private class OpportunityDailyAggregateRow
    {
        public DateOnly ScanDate { get; set; }
        public double AvgScore { get; set; }
        public int Count { get; set; }
    }
}
