using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class BrandPulseSnapshotRepository : IBrandPulseSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public BrandPulseSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
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
            "DELETE FROM BrandPulseScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task InsertSummaryAsync(BrandPulseScanSummary summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO BrandPulseScanSummaries
                (OrganizationId, ScanDate, BrandHealth, AiConfidence, MessagingConsistency, BrandTrust,
                 SentimentPositive, SentimentNeutral, SentimentNegative,
                 AlertsJson, ModelInsightsJson, AccuracyFlagsJson, PromptEvidenceJson)
              VALUES
                (@OrganizationId, @ScanDate, @BrandHealth, @AiConfidence, @MessagingConsistency, @BrandTrust,
                 @SentimentPositive, @SentimentNeutral, @SentimentNegative,
                 @AlertsJson::jsonb, @ModelInsightsJson::jsonb, @AccuracyFlagsJson::jsonb, @PromptEvidenceJson::jsonb)",
            summary);
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM BrandPulseScanSummaries WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<BrandPulseScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<BrandPulseScanSummary>(
            "SELECT * FROM BrandPulseScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task<List<BrandPulseScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<BrandPulseScanSummary>(
            @"SELECT * FROM BrandPulseScanSummaries
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM BrandPulseScanSummaries
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }
}
