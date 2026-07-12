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
            INSERT INTO HistoricalScans (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore, HallucinationRisk, SeoHealth, AeoReadiness, GeoReadiness)
            VALUES (@OrganizationId, @ScanDate, @VisibilityScore, @CitationScore, @SentimentScore, @CompetitorScore, @HallucinationRisk, @SeoHealth, @AeoReadiness, @GeoReadiness)
            ON CONFLICT (OrganizationId, ScanDate) DO UPDATE 
            SET VisibilityScore = EXCLUDED.VisibilityScore,
                CitationScore = EXCLUDED.CitationScore,
                SentimentScore = EXCLUDED.SentimentScore,
                CompetitorScore = EXCLUDED.CompetitorScore,
                HallucinationRisk = EXCLUDED.HallucinationRisk,
                SeoHealth = EXCLUDED.SeoHealth,
                AeoReadiness = EXCLUDED.AeoReadiness,
                GeoReadiness = EXCLUDED.GeoReadiness
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

    public async Task<Guid> InsertGeoPillarAsync(GeoPillar pillar)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO GeoPillars (OrganizationId, ScanDate, PillarKey, Label, Description, Score)
            VALUES (@OrganizationId, @ScanDate, @PillarKey, @Label, @Description, @Score)
            ON CONFLICT (OrganizationId, ScanDate, PillarKey) DO UPDATE
            SET Score = EXCLUDED.Score,
                Label = EXCLUDED.Label,
                Description = EXCLUDED.Description
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, pillar);
    }

    public async Task<List<GeoPillar>> GetGeoPillarsByOrgAsync(Guid organizationId, DateOnly? fromDate = null)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM GeoPillars WHERE OrganizationId = @OrganizationId";
        if (fromDate.HasValue) sql += " AND ScanDate >= @FromDate";
        sql += " ORDER BY ScanDate ASC;";
        var results = await connection.QueryAsync<GeoPillar>(sql, new { OrganizationId = organizationId, FromDate = fromDate });
        return results.ToList();
    }

    public async Task<Guid> InsertPromptCoverageAsync(PromptCoverage coverage)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO PromptCoverages (OrganizationId, ScanDate, PromptType, Example, Note, Percentage, Direction)
            VALUES (@OrganizationId, @ScanDate, @PromptType, @Example, @Note, @Percentage, @Direction)
            ON CONFLICT (OrganizationId, ScanDate, PromptType) DO UPDATE
            SET Percentage = EXCLUDED.Percentage,
                Example = EXCLUDED.Example,
                Note = EXCLUDED.Note,
                Direction = EXCLUDED.Direction
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, coverage);
    }

    public async Task<List<PromptCoverage>> GetPromptCoverageByOrgAsync(Guid organizationId, DateOnly? fromDate = null)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM PromptCoverages WHERE OrganizationId = @OrganizationId";
        if (fromDate.HasValue) sql += " AND ScanDate >= @FromDate";
        sql += " ORDER BY ScanDate ASC;";
        var results = await connection.QueryAsync<PromptCoverage>(sql, new { OrganizationId = organizationId, FromDate = fromDate });
        return results.ToList();
    }

    public async Task<Guid> InsertWinLossEventAsync(WinLossEvent winLoss)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO WinLossEvents (OrganizationId, Timestamp, Type, Title, Engine)
            VALUES (@OrganizationId, @Timestamp, @Type, @Title, @Engine)
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, winLoss);
    }

    public async Task<List<WinLossEvent>> GetWinLossEventsByOrgAsync(Guid organizationId, int limit = 10)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = "SELECT * FROM WinLossEvents WHERE OrganizationId = @OrganizationId ORDER BY Timestamp DESC LIMIT @Limit;";
        var results = await connection.QueryAsync<WinLossEvent>(sql, new { OrganizationId = organizationId, Limit = limit });
        return results.ToList();
    }

    public async Task EnsureGeoTablesCreatedAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            CREATE TABLE IF NOT EXISTS GeoPillars (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                PillarKey TEXT NOT NULL,
                Label TEXT NOT NULL,
                Description TEXT NOT NULL,
                Score INT NOT NULL,
                UNIQUE (OrganizationId, ScanDate, PillarKey)
            );

            CREATE TABLE IF NOT EXISTS PromptCoverages (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                ScanDate DATE NOT NULL,
                PromptType TEXT NOT NULL,
                Example TEXT NOT NULL,
                Note TEXT NOT NULL,
                Percentage INT NOT NULL,
                Direction TEXT NOT NULL,
                UNIQUE (OrganizationId, ScanDate, PromptType)
            );

            CREATE TABLE IF NOT EXISTS WinLossEvents (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                Timestamp TIMESTAMPTZ NOT NULL,
                Type TEXT NOT NULL,
                Title TEXT NOT NULL,
                Engine TEXT NOT NULL
            );
        ";
        await connection.ExecuteAsync(sql);

        // Self-heal the timestamp column if it was created as timestamp without time zone
        try 
        {
            await connection.ExecuteAsync("ALTER TABLE WinLossEvents ALTER COLUMN Timestamp TYPE TIMESTAMPTZ USING Timestamp AT TIME ZONE 'UTC';");
        }
        catch { /* Ignore if it's already TIMESTAMPTZ or fails */ }
    }

    public async Task<List<Guid>> GetAllOrganizationIdsAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<Guid>("SELECT Id FROM Organizations;");
        return results.ToList();
    }
}
