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

    public async Task<List<Competitor>> GetCompetitorsByOrgAsync(Guid organizationId, int limit = 100)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 500);
        var graphTablesExist = await connection.ExecuteScalarAsync<bool>(@"
            SELECT
                EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'websites')
                AND EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'company')
                AND EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'companycompetitor')");

        if (graphTablesExist)
        {
            var graphResults = (await connection.QueryAsync<Competitor>(@"
                WITH org_company AS (
                    SELECT CompanyId
                    FROM Websites
                    WHERE OrganizationId = @OrganizationId AND CompanyId IS NOT NULL
                    ORDER BY CreatedAt DESC
                    LIMIT 1
                )
                SELECT
                    comp.Id AS Id,
                    @OrganizationId AS OrganizationId,
                    c.CompanyName AS Name,
                    c.Website AS WebsiteUrl,
                    COALESCE(c.Industry, '') AS Industry,
                    cc.Reason AS Description,
                    'Direct' AS Category,
                    NULL AS Logo,
                    NULL AS Country,
                    0 AS Authority,
                    0 AS Popularity,
                    cc.Rank AS Rank,
                    ROUND(cc.Similarity)::int AS SimilarityScore,
                    jsonb_build_object(
                        'source', 'CompanyCompetitor',
                        'discoverySource', cc.DiscoverySource,
                        'similarity', cc.Similarity,
                        'confidence', cc.Confidence,
                        'reason', cc.Reason,
                        'strength', cc.Strength,
                        'weakness', cc.Weakness
                    )::text AS RawJson,
                    'Completed' AS EnrichmentStatus,
                    c.BusinessProfileJson::text AS EnrichedJson,
                    c.LastAnalyzedAt AS EnrichedAt,
                    'Direct' AS CompetitorType,
                    cc.Confidence AS Confidence,
                    cc.CreatedAt AS CreatedAt
                FROM org_company oc
                JOIN CompanyCompetitor cc ON cc.CompanyId = oc.CompanyId
                JOIN Company c ON c.Id = cc.CompetitorCompanyId
                JOIN LATERAL (
                    SELECT Id
                    FROM Competitors comp
                    WHERE comp.OrganizationId = @OrganizationId
                      AND (
                          (NULLIF(c.Website, '') IS NOT NULL AND LOWER(COALESCE(comp.WebsiteUrl, '')) = LOWER(c.Website))
                          OR LOWER(comp.Name) = LOWER(c.CompanyName)
                      )
                    ORDER BY comp.CreatedAt DESC
                    LIMIT 1
                ) comp ON TRUE
                ORDER BY cc.Rank, cc.Similarity DESC
                LIMIT @Limit",
                new { OrganizationId = organizationId, Limit = limit })).ToList();

            if (graphResults.Count > 0) return graphResults;
        }

        var sql = "SELECT * FROM Competitors WHERE OrganizationId = @OrganizationId ORDER BY Authority DESC LIMIT @Limit;";
        var results = await connection.QueryAsync<Competitor>(sql, new { OrganizationId = organizationId, Limit = limit });
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
            INSERT INTO HistoricalScans (OrganizationId, ScanDate, VisibilityScore, CitationScore, SentimentScore, CompetitorScore, HallucinationRisk, SeoHealth, AeoReadiness, GeoReadiness, ScoringMethodVersion)
            VALUES (@OrganizationId, @ScanDate, @VisibilityScore, @CitationScore, @SentimentScore, @CompetitorScore, @HallucinationRisk, @SeoHealth, @AeoReadiness, @GeoReadiness, @ScoringMethodVersion)
            ON CONFLICT (OrganizationId, ScanDate) DO UPDATE
            SET VisibilityScore = EXCLUDED.VisibilityScore,
                CitationScore = EXCLUDED.CitationScore,
                SentimentScore = EXCLUDED.SentimentScore,
                CompetitorScore = EXCLUDED.CompetitorScore,
                HallucinationRisk = EXCLUDED.HallucinationRisk,
                SeoHealth = EXCLUDED.SeoHealth,
                AeoReadiness = EXCLUDED.AeoReadiness,
                GeoReadiness = EXCLUDED.GeoReadiness,
                ScoringMethodVersion = EXCLUDED.ScoringMethodVersion
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, scan);
    }

    public async Task<List<HistoricalScan>> GetHistoricalScansByOrgAsync(Guid organizationId, int limit = 365)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 1095);
        var sql = @"
            SELECT * FROM (
                SELECT * FROM HistoricalScans
                WHERE OrganizationId = @OrganizationId
                ORDER BY ScanDate DESC
                LIMIT @Limit
            ) recent
            ORDER BY ScanDate ASC;";
        var results = await connection.QueryAsync<HistoricalScan>(sql, new { OrganizationId = organizationId, Limit = limit });
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

    public async Task<List<ShareOfVoice>> GetShareOfVoiceByOrgAsync(Guid organizationId, int limit = 1000)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 5000);
        var sql = @"
            SELECT * FROM (
                SELECT * FROM ShareOfVoice
                WHERE OrganizationId = @OrganizationId
                ORDER BY ScanDate DESC, SharePercentage DESC
                LIMIT @Limit
            ) recent
            ORDER BY ScanDate ASC, SharePercentage DESC;";
        var results = await connection.QueryAsync<ShareOfVoice>(sql, new { OrganizationId = organizationId, Limit = limit });
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

    public async Task<List<GeoPillar>> GetGeoPillarsByOrgAsync(Guid organizationId, DateOnly? fromDate = null, int limit = 1000)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 5000);
        var sql = "SELECT * FROM GeoPillars WHERE OrganizationId = @OrganizationId";
        if (fromDate.HasValue) sql += " AND ScanDate >= @FromDate";
        sql += " ORDER BY ScanDate ASC LIMIT @Limit;";
        var results = await connection.QueryAsync<GeoPillar>(sql, new { OrganizationId = organizationId, FromDate = fromDate, Limit = limit });
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

    public async Task<List<PromptCoverage>> GetPromptCoverageByOrgAsync(Guid organizationId, DateOnly? fromDate = null, int limit = 1000)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        limit = Math.Clamp(limit, 1, 5000);
        var sql = "SELECT * FROM PromptCoverages WHERE OrganizationId = @OrganizationId";
        if (fromDate.HasValue) sql += " AND ScanDate >= @FromDate";
        sql += " ORDER BY ScanDate ASC LIMIT @Limit;";
        var results = await connection.QueryAsync<PromptCoverage>(sql, new { OrganizationId = organizationId, FromDate = fromDate, Limit = limit });
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
        limit = Math.Clamp(limit, 1, 100);
        var sql = "SELECT * FROM WinLossEvents WHERE OrganizationId = @OrganizationId ORDER BY Timestamp DESC LIMIT @Limit;";
        var results = await connection.QueryAsync<WinLossEvent>(sql, new { OrganizationId = organizationId, Limit = limit });
        return results.ToList();
    }

    public Task EnsureGeoTablesCreatedAsync()
    {
        return Task.CompletedTask;
    }

    public async Task<List<Guid>> GetAllOrganizationIdsAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<Guid>("SELECT Id FROM Organizations;");
        return results.ToList();
    }
}
