using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class CommandCenterInsightRepository : ICommandCenterInsightRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CommandCenterInsightRepository(IDbConnectionFactory dbConnectionFactory)
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
            "DELETE FROM CommandCenterInsightSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task InsertAsync(CommandCenterInsightSnapshot snapshot)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "INSERT INTO CommandCenterInsightSnapshots (OrganizationId, ScanDate, InsightsJson) VALUES (@OrganizationId, @ScanDate, @InsightsJson::jsonb)",
            snapshot);
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CommandCenterInsightSnapshots WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<CommandCenterInsightSnapshot?> GetLatestAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<CommandCenterInsightSnapshot>(
            "SELECT * FROM CommandCenterInsightSnapshots WHERE OrganizationId = @OrganizationId ORDER BY ScanDate DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }
}
