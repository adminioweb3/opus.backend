using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class CitationScanSnapshotRepository : ICitationScanSnapshotRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public CitationScanSnapshotRepository(IDbConnectionFactory dbConnectionFactory)
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
            "DELETE FROM CitationScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        await connection.ExecuteAsync(
            "DELETE FROM CitationSourceSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task InsertSummaryAsync(CitationScanSummary summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO CitationScanSummaries
                (OrganizationId, ScanDate, CompositeQualityScore, AverageAuthorityScore, AverageInfluenceScore, CitationSignal, ModelsReferencingCount, ModelsTrackedCount)
              VALUES
                (@OrganizationId, @ScanDate, @CompositeQualityScore, @AverageAuthorityScore, @AverageInfluenceScore, @CitationSignal, @ModelsReferencingCount, @ModelsTrackedCount)",
            summary);
    }

    public async Task InsertSourceSnapshotAsync(CitationSourceSnapshot snapshot)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO CitationSourceSnapshots
                (OrganizationId, ScanDate, Source, Category, AuthorityScore, InfluenceScore, CitationFrequency, CompetitorCoverage, OpportunityScore, MentionProbability, Reason)
              VALUES
                (@OrganizationId, @ScanDate, @Source, @Category, @AuthorityScore, @InfluenceScore, @CitationFrequency, @CompetitorCoverage, @OpportunityScore, @MentionProbability, @Reason)",
            snapshot);
    }

    public async Task<DateOnly?> GetLatestScanDateAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var result = await connection.ExecuteScalarAsync(
            "SELECT MAX(ScanDate) FROM CitationScanSummaries WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });

        return result switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => null
        };
    }

    public async Task<CitationScanSummary?> GetSummaryByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<CitationScanSummary>(
            "SELECT * FROM CitationScanSummaries WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate",
            new { OrganizationId = organizationId, ScanDate = scanDate });
    }

    public async Task<List<CitationSourceSnapshot>> GetSourceSnapshotsByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<CitationSourceSnapshot>(
            "SELECT * FROM CitationSourceSnapshots WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate ORDER BY InfluenceScore DESC",
            new { OrganizationId = organizationId, ScanDate = scanDate });
        return results.ToList();
    }

    public async Task<List<CitationScanSummary>> GetRecentSummaryHistoryAsync(Guid organizationId, int maxScanDates = 13)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<CitationScanSummary>(
            @"SELECT * FROM CitationScanSummaries
              WHERE OrganizationId = @OrganizationId
                AND ScanDate IN (
                    SELECT DISTINCT ScanDate FROM CitationScanSummaries
                    WHERE OrganizationId = @OrganizationId
                    ORDER BY ScanDate DESC
                    LIMIT @MaxScanDates
                )
              ORDER BY ScanDate ASC",
            new { OrganizationId = organizationId, MaxScanDates = maxScanDates });
        return results.ToList();
    }
}
