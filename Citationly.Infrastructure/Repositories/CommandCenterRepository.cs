using System.Data;
using System.Text.Json;
using Dapper;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Repositories;

public class CommandCenterRepository : ICommandCenterRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CommandCenterRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    private static async Task EnsureSchemaAsync(IDbConnection connection)
    {
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CommandCenterInsightSnapshots (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                InsightsJson JSONB NOT NULL DEFAULT '[]'::jsonb,
                CreatedAt TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS idx_commandcenterinsightsnapshots_org_scandate
                ON CommandCenterInsightSnapshots (OrganizationId, ScanDate);
        ");
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var raw = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CommandCenterInsightSnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return raw switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<List<string>> GetLatestInsightsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        try
        {
            var json = await connection.ExecuteScalarAsync<string?>(@"
                SELECT InsightsJson::text
                FROM CommandCenterInsightSnapshots
                WHERE OrganizationId = @OrganizationId
                ORDER BY ScanDate DESC, CreatedAt DESC
                LIMIT 1",
                new { OrganizationId = organizationId });

            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            var insights = JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return insights ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task SaveInsightsAsync(Guid organizationId, DateOnly scanDate, List<string> insights)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await EnsureSchemaAsync(connection);

        var insightsJson = JsonSerializer.Serialize(insights);

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(
                "DELETE FROM CommandCenterInsightSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
                new { OrganizationId = organizationId, ScanDate = scanDate }, transaction: transaction);

            await connection.ExecuteAsync(@"
                INSERT INTO CommandCenterInsightSnapshots (Id, OrganizationId, ScanDate, InsightsJson, CreatedAt)
                VALUES (@Id, @OrganizationId, @ScanDate, @InsightsJson::jsonb, @CreatedAt)",
                new
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    ScanDate = scanDate,
                    InsightsJson = insightsJson,
                    CreatedAt = DateTime.UtcNow
                }, transaction: transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<CommandCenterSiblingData> GetSiblingSnapshotsAsync(Guid organizationId, int rangeDays)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        var data = new CommandCenterSiblingData
        {
            Visibility = await GetMetricAsync(connection,
                "visibilityscansummaries", "CompositeScore", null,
                organizationId, rangeDays),

            CitationQuality = await GetMetricAsync(connection,
                "citationscansummaries", "CompositeQualityScore", null,
                organizationId, rangeDays),

            BrandHealth = await GetMetricAsync(connection,
                "brandpulsescansummaries", "BrandHealth", null,
                organizationId, rangeDays),

            ShareOfVoice = await GetMetricAsync(connection,
                "competitorsnapshots", "ShareOfVoice", "IsYou = true",
                organizationId, rangeDays)
        };

        try
        {
            var flags = await connection.QueryFirstOrDefaultAsync<BrandPulseFlagsRow>(@"
                SELECT AccuracyFlagsJson::text AS AccuracyFlagsJson, AlertsJson::text AS AlertsJson
                FROM brandpulsescansummaries
                WHERE OrganizationId = @OrganizationId
                ORDER BY ScanDate DESC
                LIMIT 1",
                new { OrganizationId = organizationId });

            data.BrandPulseAccuracyFlagsJson = flags?.AccuracyFlagsJson;
            data.BrandPulseAlertsJson = flags?.AlertsJson;
        }
        catch
        {
            data.BrandPulseAccuracyFlagsJson = null;
            data.BrandPulseAlertsJson = null;
        }

        return data;
    }

    /// <summary>
    /// Reads a single-metric column from a sibling scan-summary table. Current/previous always come from
    /// the two most recent rows overall (regardless of range); History is bounded to the last
    /// <paramref name="rangeDays"/> days, oldest-first, for trend/sparkline display.
    /// table/column/extraWhere are fixed internal constants (never user input) so string interpolation is safe here.
    /// </summary>
    private static async Task<CommandCenterMetric> GetMetricAsync(
        IDbConnection connection, string table, string column, string? extraWhere, Guid organizationId, int rangeDays)
    {
        try
        {
            var whereClause = string.IsNullOrEmpty(extraWhere)
                ? "WHERE OrganizationId = @OrganizationId"
                : $"WHERE OrganizationId = @OrganizationId AND {extraWhere}";

            var latestTwo = (await connection.QueryAsync<ScanPointRow>(
                $"SELECT ScanDate, {column} AS Value FROM {table} {whereClause} ORDER BY ScanDate DESC LIMIT 2",
                new { OrganizationId = organizationId })).ToList();

            if (latestTwo.Count == 0)
            {
                return new CommandCenterMetric { HasData = false };
            }

            var current = latestTwo[0].Value;
            var previous = latestTwo.Count > 1 ? latestTwo[1].Value : latestTwo[0].Value;

            var historyRows = (await connection.QueryAsync<ScanPointRow>(
                $@"SELECT ScanDate, {column} AS Value FROM {table} {whereClause}
                   AND ScanDate >= CURRENT_DATE - make_interval(days => @RangeDays)
                   ORDER BY ScanDate ASC LIMIT 100",
                new { OrganizationId = organizationId, RangeDays = rangeDays })).ToList();

            var history = historyRows.Count > 0
                ? historyRows.Select(r => r.Value).ToList()
                : new List<double> { current };

            // Bound the sparkline to the last ~8 scan points (matches the KPI sparkline UI expectation
            // and the capping convention used by sibling features' sparkline/trend queries), even though
            // the range filter above may have selected more rows than that for a wide range like 90d.
            if (history.Count > 8)
            {
                history = history.Skip(history.Count - 8).ToList();
            }

            return new CommandCenterMetric
            {
                HasData = true,
                Current = current,
                Previous = previous,
                History = history,
                LatestScanDate = latestTwo[0].ScanDate
            };
        }
        catch
        {
            return new CommandCenterMetric { HasData = false };
        }
    }

    private class ScanPointRow
    {
        public DateOnly ScanDate { get; set; }
        public double Value { get; set; }
    }

    private class BrandPulseFlagsRow
    {
        public string? AccuracyFlagsJson { get; set; }
        public string? AlertsJson { get; set; }
    }
}
