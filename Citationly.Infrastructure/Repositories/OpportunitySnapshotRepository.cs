using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class OpportunitySnapshotRepository : IOpportunitySnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public OpportunitySnapshotRepository(IDbConnectionFactory dbConnectionFactory)
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
            "DELETE FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task InsertAsync(OpportunitySnapshot snapshot)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO OpportunitySnapshots
                (OrganizationId, ScanDate, OpportunityKey, Category, Title, Summary, WhyItMatters, Score, Effort, EstimatedGainPct, Eta, CompetitorContext, ChecklistJson)
              VALUES
                (@OrganizationId, @ScanDate, @OpportunityKey, @Category, @Title, @Summary, @WhyItMatters, @Score, @Effort, @EstimatedGainPct, @Eta, @CompetitorContext, @ChecklistJson::jsonb)",
            snapshot);
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<List<OpportunitySnapshot>> GetSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<OpportunitySnapshot>(
            "SELECT * FROM OpportunitySnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate ORDER BY Score DESC",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        return results.ToList();
    }

    public async Task<List<OpportunitySnapshot>> GetRecentHistoryAsync(Guid organizationId, int maxScanDates = 13)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<OpportunitySnapshot>(
            @"SELECT * FROM OpportunitySnapshots
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM OpportunitySnapshots
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }
}
