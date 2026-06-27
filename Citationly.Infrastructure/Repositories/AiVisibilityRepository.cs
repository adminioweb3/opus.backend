using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class AiVisibilityRepository : IAiVisibilityRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AiVisibilityRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> InsertCompetitorAsync(Competitor competitor)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO Competitors (OrganizationId, Name, WebsiteUrl, Industry, Description, Category, Logo, Country, Authority, Popularity)
            VALUES (@OrganizationId, @Name, @WebsiteUrl, @Industry, @Description, @Category, @Logo, @Country, @Authority, @Popularity)
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, competitor);
    }

    public async Task<List<Competitor>> GetCompetitorsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM Competitors WHERE OrganizationId = @OrganizationId ORDER BY Authority DESC;";
        var results = await connection.QueryAsync<Competitor>(sql, new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task DeleteCompetitorsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM Competitors WHERE OrganizationId = @OrganizationId;", new { OrganizationId = organizationId });
    }

    public async Task<Guid> InsertHistoricalScanAsync(HistoricalScan scan)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO HistoricalScans (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore)
            VALUES (@OrganizationId, @ScanDate, @VisibilityScore, @CitationScore, @SentimentScore, @CompetitorScore)
            ON CONFLICT (OrganizationId, ScanDate) DO UPDATE 
            SET VisibilityScore = EXCLUDED.VisibilityScore,
                CitationScore = EXCLUDED.CitationScore,
                SentimentScore = EXCLUDED.SentimentScore,
                CompetitorScore = EXCLUDED.CompetitorScore
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, scan);
    }

    public async Task<List<HistoricalScan>> GetHistoricalScansByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM HistoricalScans WHERE OrganizationId = @OrganizationId ORDER BY ScanDate ASC;";
        var results = await connection.QueryAsync<HistoricalScan>(sql, new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task<Guid> InsertShareOfVoiceAsync(ShareOfVoice share)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO ShareOfVoice (OrganizationId, ScanDate, CompetitorName, SharePercentage, ColorCode)
            VALUES (@OrganizationId, @ScanDate, @CompetitorName, @SharePercentage, @ColorCode)
            ON CONFLICT ON CONSTRAINT shareofvoice_organizationid_scandate_competitorname_key DO UPDATE
            SET SharePercentage = EXCLUDED.SharePercentage,
                ColorCode = EXCLUDED.ColorCode
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, share);
    }

    public async Task<List<ShareOfVoice>> GetShareOfVoiceByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM ShareOfVoice WHERE OrganizationId = @OrganizationId ORDER BY ScanDate ASC, SharePercentage DESC;";
        var results = await connection.QueryAsync<ShareOfVoice>(sql, new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task DeleteShareOfVoiceByScanDateAsync(Guid organizationId, DateOnly scanDate)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM ShareOfVoice WHERE OrganizationId = @OrganizationId AND ScanDate = @ScanDate;", new { OrganizationId = organizationId, ScanDate = scanDate });
    }
}
