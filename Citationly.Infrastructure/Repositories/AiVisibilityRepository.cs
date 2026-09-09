using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;
using System.Data;

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
            await EnsureGraphCompetitorsMaterializedAsync(connection, organizationId);

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
                        'discoverySource', 'graph',
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

        var sql = @"
            WITH latest_profile AS (
                SELECT NULLIF(BusinessName, '') AS BusinessName, NULLIF(WebsiteUrl, '') AS WebsiteUrl
                FROM WebsiteProfiles
                WHERE OrganizationId = @OrganizationId
                ORDER BY CreatedAt DESC
                LIMIT 1
            )
            SELECT comp.*
            FROM Competitors comp
            LEFT JOIN latest_profile p ON TRUE
            WHERE comp.OrganizationId = @OrganizationId
              AND (
                  p.BusinessName IS NULL
                  OR LOWER(comp.Name) <> LOWER(p.BusinessName)
              )
              AND (
                  p.WebsiteUrl IS NULL
                  OR LOWER(TRIM(TRAILING '/' FROM COALESCE(comp.WebsiteUrl, ''))) <> LOWER(TRIM(TRAILING '/' FROM p.WebsiteUrl))
              )
            ORDER BY comp.Authority DESC
            LIMIT @Limit;";
        var results = await connection.QueryAsync<Competitor>(sql, new { OrganizationId = organizationId, Limit = limit });
        return results.ToList();
    }

    private static Task EnsureGraphCompetitorsMaterializedAsync(IDbConnection connection, Guid organizationId)
    {
        return connection.ExecuteAsync(@"
            WITH org_company AS (
                SELECT CompanyId
                FROM Websites
                WHERE OrganizationId = @OrganizationId AND CompanyId IS NOT NULL
                ORDER BY CreatedAt DESC
                LIMIT 1
            ),
            graph_competitors AS (
                SELECT
                    @OrganizationId AS OrganizationId,
                    c.CompanyName AS Name,
                    c.Website AS WebsiteUrl,
                    COALESCE(c.Industry, '') AS Industry,
                    cc.Reason AS Description,
                    'Direct' AS Category,
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
                    ) AS RawJson,
                    'Completed' AS EnrichmentStatus,
                    c.BusinessProfileJson AS EnrichedJson,
                    c.LastAnalyzedAt AS EnrichedAt,
                    'Direct' AS CompetitorType,
                    cc.Confidence AS Confidence,
                    cc.CreatedAt AS CreatedAt
                FROM org_company oc
                JOIN CompanyCompetitor cc ON cc.CompanyId = oc.CompanyId
                JOIN Company c ON c.Id = cc.CompetitorCompanyId
            )
            INSERT INTO Competitors (
                OrganizationId, Name, WebsiteUrl, Industry, Description, Category, Authority, Popularity,
                Rank, SimilarityScore, RawJson, EnrichmentStatus, EnrichedJson, EnrichedAt,
                CompetitorType, Confidence, CreatedAt)
            SELECT
                gc.OrganizationId, gc.Name, gc.WebsiteUrl, gc.Industry, gc.Description, gc.Category,
                gc.Authority, gc.Popularity, gc.Rank, gc.SimilarityScore, gc.RawJson,
                gc.EnrichmentStatus, gc.EnrichedJson, gc.EnrichedAt, gc.CompetitorType,
                gc.Confidence, gc.CreatedAt
            FROM graph_competitors gc
            WHERE NOT EXISTS (
                SELECT 1
                FROM Competitors existing
                WHERE existing.OrganizationId = @OrganizationId
                  AND (
                      (NULLIF(gc.WebsiteUrl, '') IS NOT NULL AND LOWER(COALESCE(existing.WebsiteUrl, '')) = LOWER(gc.WebsiteUrl))
                      OR LOWER(existing.Name) = LOWER(gc.Name)
                  )
            );",
            new { OrganizationId = organizationId });
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
