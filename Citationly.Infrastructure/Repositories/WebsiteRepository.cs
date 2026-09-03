using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class WebsiteRepository : IWebsiteRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public WebsiteRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> GetOrInsertWebsiteAsync(Guid organizationId, string domainUrl)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var websiteId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT Id FROM Websites WHERE DomainUrl = @DomainUrl AND OrganizationId = @OrganizationId",
            new { DomainUrl = domainUrl, OrganizationId = organizationId });

        if (websiteId == null || websiteId == Guid.Empty)
        {
            websiteId = await connection.ExecuteScalarAsync<Guid>(
                "INSERT INTO Websites (OrganizationId, DomainUrl) VALUES (@OrganizationId, @DomainUrl) RETURNING Id",
                new { OrganizationId = organizationId, DomainUrl = domainUrl });
        }
        
        return websiteId.Value;
    }

    public async Task<IEnumerable<Website>> GetAllWebsitesAsync()
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Website>("SELECT * FROM Websites");
    }

    public async Task<IEnumerable<Website>> GetWebsitesByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Website>(
            "SELECT * FROM Websites WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task LinkWebsiteToCompanyAsync(Guid websiteId, Guid companyId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Websites SET CompanyId = @CompanyId WHERE Id = @WebsiteId",
            new { WebsiteId = websiteId, CompanyId = companyId });
    }

    public async Task<Website> ConnectWebsiteAsync(Guid organizationId, string domainUrl, string platformName)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO Websites (OrganizationId, DomainUrl, PlatformName, HealthScore, VisibilityScore, Status) 
              VALUES (@OrganizationId, @DomainUrl, @PlatformName, 100, 0, 'Connected') 
              RETURNING Id",
            new { OrganizationId = organizationId, DomainUrl = domainUrl, PlatformName = platformName });

        return new Website
        {
            Id = id,
            OrganizationId = organizationId,
            DomainUrl = domainUrl,
            PlatformName = platformName,
            HealthScore = 100,
            VisibilityScore = 0,
            Status = "Connected",
            CreatedAt = DateTime.UtcNow
        };
    }

    public async Task<Guid> InsertCrawledPageAsync(CrawledPage page)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_InsertCrawledPage(@WebsiteId, @Url, @Title, @Content)",
            new { page.WebsiteId, page.Url, page.Title, page.Content });
    }

    public async Task<Guid> InsertRecommendationAsync(Recommendation rec)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_InsertRecommendation(@WebsiteId, @CrawledPageId, @Title, @Description, @ActionType, @Priority)",
            new { rec.WebsiteId, rec.CrawledPageId, rec.Title, rec.Description, rec.ActionType, rec.Priority });
    }

    public async Task<Recommendation?> GetRecommendationByIdAsync(Guid id, Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Recommendation>(
            @"SELECT r.* FROM Recommendations r
              JOIN Websites w ON r.WebsiteId = w.Id
              WHERE r.Id = @Id AND w.OrganizationId = @OrganizationId",
            new { Id = id, OrganizationId = organizationId });
    }

    public async Task UpdateRecommendationStatusAsync(Guid id, string status, string? deployedUrl)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "UPDATE Recommendations SET Status = @Status, DeployedUrl = @DeployedUrl WHERE Id = @Id",
            new { Id = id, Status = status, DeployedUrl = deployedUrl });
    }

    public async Task<Guid> InsertWebsiteProfileAsync(WebsiteProfile profile)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        var id = await connection.ExecuteScalarAsync<Guid>(@"
            INSERT INTO WebsiteProfiles (OrganizationId, WebsiteUrl, BusinessName, RawProfileJson, CreatedAt)
            VALUES (@OrganizationId, @WebsiteUrl, @BusinessName, @RawProfileJson::jsonb, @CreatedAt)
            RETURNING Id",
            new { 
                profile.OrganizationId, 
                profile.WebsiteUrl, 
                profile.BusinessName, 
                profile.RawProfileJson, 
                CreatedAt = profile.CreatedAt == default ? DateTime.UtcNow : profile.CreatedAt 
            });

        return id;
    }

    public async Task<WebsiteProfile?> GetLatestWebsiteProfileAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        return await connection.QueryFirstOrDefaultAsync<WebsiteProfile>(@"
            SELECT * FROM WebsiteProfiles
            WHERE OrganizationId = @OrganizationId
            ORDER BY CreatedAt DESC
            LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task InsertCompetitorsAsync(IEnumerable<Competitor> competitors)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var comp in competitors)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO Competitors (OrganizationId, Name, WebsiteUrl, Industry, Description, Category, Logo, Country, Authority, Popularity, Rank, SimilarityScore, RawJson, EnrichmentStatus, EnrichedJson, EnrichedAt, CompetitorType, Confidence, CreatedAt)
                    VALUES (@OrganizationId, @Name, @WebsiteUrl, @Industry, @Description, @Category, @Logo, @Country, @Authority, @Popularity, @Rank, @SimilarityScore, @RawJson::jsonb, @EnrichmentStatus, @EnrichedJson::jsonb, @EnrichedAt, @CompetitorType, @Confidence, @CreatedAt)",
                    new {
                        comp.OrganizationId,
                        comp.Name,
                        comp.WebsiteUrl,
                        comp.Industry,
                        comp.Description,
                        comp.Category,
                        comp.Logo,
                        comp.Country,
                        comp.Authority,
                        comp.Popularity,
                        comp.Rank,
                        comp.SimilarityScore,
                        comp.RawJson,
                        comp.EnrichmentStatus,
                        EnrichedJson = comp.EnrichedJson ?? (object)DBNull.Value,
                        EnrichedAt = comp.EnrichedAt.HasValue ? (object)comp.EnrichedAt.Value : DBNull.Value,
                        comp.CompetitorType,
                        comp.Confidence,
                        CreatedAt = comp.CreatedAt == default ? DateTime.UtcNow : comp.CreatedAt
                    }, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<Competitor>> GetCompetitorsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var graphTablesExist = await connection.ExecuteScalarAsync<bool>(@"
            SELECT
                EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'websites')
                AND EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'company')
                AND EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'companycompetitor')");

        if (graphTablesExist)
        {
            var graphRows = (await connection.QueryAsync<Competitor>(@"
                WITH org_company AS (
                    SELECT CompanyId
                    FROM Websites
                    WHERE OrganizationId = @OrganizationId AND CompanyId IS NOT NULL
                    ORDER BY CreatedAt DESC
                    LIMIT 1
                )
                SELECT
                    cc.Id AS Id,
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
                ORDER BY cc.Rank, cc.Similarity DESC",
                new { OrganizationId = organizationId })).ToList();

            if (graphRows.Count > 0) return graphRows;
        }

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'competitors')"
        );
        if (!exists) return Enumerable.Empty<Competitor>();

        return await connection.QueryAsync<Competitor>(
            "SELECT * FROM Competitors WHERE OrganizationId = @OrganizationId ORDER BY SimilarityScore DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<int> GetCompetitorCountAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var graphTablesExist = await connection.ExecuteScalarAsync<bool>(@"
            SELECT
                EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'websites')
                AND EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'companycompetitor')");

        if (graphTablesExist)
        {
            var graphCount = await connection.ExecuteScalarAsync<int>(@"
                WITH org_company AS (
                    SELECT CompanyId
                    FROM Websites
                    WHERE OrganizationId = @OrganizationId AND CompanyId IS NOT NULL
                    ORDER BY CreatedAt DESC
                    LIMIT 1
                )
                SELECT COUNT(1)
                FROM org_company oc
                JOIN CompanyCompetitor cc ON cc.CompanyId = oc.CompanyId",
                new { OrganizationId = organizationId });

            if (graphCount > 0) return graphCount;
        }

        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'competitors')"
        );
        if (!exists) return 0;

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Competitors WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task UpdateCompetitorAsync(Competitor competitor)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE Competitors SET
                EnrichmentStatus = @EnrichmentStatus,
                EnrichedJson = @EnrichedJson::jsonb,
                EnrichedAt = @EnrichedAt,
                Authority = @Authority,
                Country = @Country,
                RawJson = @RawJson::jsonb
            WHERE Id = @Id",
            new {
                competitor.Id,
                competitor.EnrichmentStatus,
                EnrichedJson = competitor.EnrichedJson ?? (object)DBNull.Value,
                EnrichedAt = competitor.EnrichedAt.HasValue ? (object)competitor.EnrichedAt.Value : DBNull.Value,
                competitor.Authority,
                competitor.Country,
                competitor.RawJson
            });
    }

    public async Task DeleteCompetitorsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            "DELETE FROM Competitors WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task<Competitor?> GetCompetitorByIdAsync(Guid competitorId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Competitor>(
            "SELECT * FROM Competitors WHERE Id = @Id",
            new { Id = competitorId });
    }

    public async Task<int> GetAiSearchPromptCountAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM AiSearchPrompts WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task InsertAiSearchPromptsAsync(IEnumerable<AiSearchPrompt> prompts)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        // Schema for these columns lives in SelfHealingMigrations.cs (runs once at boot) - see
        // the note in UpdateAiSearchPromptsAsync below for why this per-call ALTER TABLE was removed.

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var prompt in prompts)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO AiSearchPrompts (
                        OrganizationId, QueryString, SearchEngine, Topic, Intent, Difficulty, Persona,
                        CommercialValue, RawJson, PromptClass, IsBranded, IsOrganicVisibilityEligible,
                        ExpectsProviderRecommendations, ExpectsBrandMention, MetricBucket, VisibilityWeight,
                        ScoringReason, ClassificationConfidence, GeneratedAt)
                    VALUES (
                        @OrganizationId, @QueryString, @SearchEngine, @Topic, @Intent, @Difficulty, @Persona,
                        @CommercialValue, @RawJson::jsonb, @PromptClass, @IsBranded, @IsOrganicVisibilityEligible,
                        @ExpectsProviderRecommendations, @ExpectsBrandMention, @MetricBucket, @VisibilityWeight,
                        @ScoringReason, @ClassificationConfidence, @GeneratedAt)",
                    new {
                        prompt.OrganizationId,
                        prompt.QueryString,
                        prompt.SearchEngine,
                        prompt.Topic,
                        prompt.Intent,
                        prompt.Difficulty,
                        prompt.Persona,
                        prompt.CommercialValue,
                        prompt.RawJson,
                        prompt.PromptClass,
                        prompt.IsBranded,
                        prompt.IsOrganicVisibilityEligible,
                        prompt.ExpectsProviderRecommendations,
                        prompt.ExpectsBrandMention,
                        prompt.MetricBucket,
                        prompt.VisibilityWeight,
                        prompt.ScoringReason,
                        prompt.ClassificationConfidence,
                        GeneratedAt = prompt.GeneratedAt == default ? DateTime.UtcNow : prompt.GeneratedAt
                    }, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<AiSearchPrompt>> GetAiSearchPromptsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<AiSearchPrompt>(
            "SELECT * FROM AiSearchPrompts WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
    }

    public async Task UpdateAiSearchPromptsVisibilityAsync(IEnumerable<AiSearchPrompt> prompts)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var prompt in prompts)
            {
                await connection.ExecuteAsync(@"
                    UPDATE AiSearchPrompts 
                    SET VisibilityScore = @VisibilityScore,
                        EstimatedRank = @EstimatedRank,
                        Confidence = @Confidence,
                        AppearsInAnswer = @AppearsInAnswer,
                        ShareOfVoiceContribution = @ShareOfVoiceContribution,
                        MentionProbability = @MentionProbability,
                        BrandStrength = @BrandStrength,
                        ContentStrength = @ContentStrength,
                        CitationStrength = @CitationStrength,
                        VisibilityReason = @VisibilityReason
                    WHERE Id = @Id",
                    prompt, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateAiSearchPromptsAsync(IEnumerable<AiSearchPrompt> prompts)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        // Schema for these columns lives in SelfHealingMigrations.cs (runs once at boot), not
        // here - this method used to run the same ALTER TABLE on every single call, which is the
        // request-time-DDL anti-pattern the roadmap's Phase 1 A2 already removed from
        // DashboardController; this was the other instance of it.

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var prompt in prompts)
            {
                await connection.ExecuteAsync(@"
                    UPDATE AiSearchPrompts 
                    SET Intent = @Intent,
                        Persona = @Persona,
                        Difficulty = @Difficulty,
                        EstimatedInterestLevel = @EstimatedInterestLevel,
                        Region = @Region,
                        Language = @Language,
                        CommercialValue = @CommercialValue,
                        TopicValidation = @TopicValidation,
                        BuyerJourneyStage = @BuyerJourneyStage,
                        PromptClass = @PromptClass,
                        IsBranded = @IsBranded,
                        IsOrganicVisibilityEligible = @IsOrganicVisibilityEligible,
                        ExpectsProviderRecommendations = @ExpectsProviderRecommendations,
                        ExpectsBrandMention = @ExpectsBrandMention,
                        MetricBucket = @MetricBucket,
                        VisibilityWeight = @VisibilityWeight,
                        ScoringReason = @ScoringReason,
                        ClassificationConfidence = @ClassificationConfidence,
                        IsEnriched = @IsEnriched,
                        RawJson = @RawJson::jsonb,
                        EnrichedAt = @EnrichedAt
                    WHERE Id = @Id",
                    prompt, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task DeleteAiSearchPromptsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM AiSearchPrompts WHERE OrganizationId = @OrganizationId", new { OrganizationId = organizationId });
    }

    public async Task InsertPlatformVisibilityAsync(VisibilitySummary summary, IEnumerable<PlatformVisibility> visibilities)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO VisibilitySummaries (Id, OrganizationId, OverallVisibilityScore, BestPlatform, WeakestPlatform, AverageMentionRate, AveragePromptCoverage, CreatedAt)
                VALUES (@Id, @OrganizationId, @OverallVisibilityScore, @BestPlatform, @WeakestPlatform, @AverageMentionRate, @AveragePromptCoverage, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var pv in visibilities)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO PlatformVisibilities (Id, OrganizationId, Platform, VisibilityScore, AverageRank, MentionRate, PromptCoverage, Confidence, StrengthsJson, WeaknessesJson, CreatedAt)
                    VALUES (@Id, @OrganizationId, @Platform, @VisibilityScore, @AverageRank, @MentionRate, @PromptCoverage, @Confidence, @StrengthsJson, @WeaknessesJson, @CreatedAt)",
                    pv, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdatePlatformVisibilityAsync(PlatformVisibility platformVisibility)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        await connection.ExecuteAsync(@"
            UPDATE PlatformVisibilities 
            SET StrengthsJson = @StrengthsJson,
                WeaknessesJson = @WeaknessesJson,
                Explanation = @Explanation,
                Confidence = @Confidence,
                IsEnriched = @IsEnriched
            WHERE Id = @Id",
            platformVisibility);
    }

    public async Task<VisibilitySummary?> GetVisibilitySummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // Fallback to avoid error if table doesn't exist
        try
        {
            return await connection.QueryFirstOrDefaultAsync<VisibilitySummary>(
                "SELECT * FROM VisibilitySummaries WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
                new { OrganizationId = organizationId });
        }
        catch
        {
            return null;
        }
    }

    public async Task<IEnumerable<PlatformVisibility>> GetPlatformVisibilitiesAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        try
        {
            return await connection.QueryAsync<PlatformVisibility>(
                "SELECT * FROM PlatformVisibilities WHERE OrganizationId = @OrganizationId",
                new { OrganizationId = organizationId });
        }
        catch
        {
            return Enumerable.Empty<PlatformVisibility>();
        }
    }

    public async Task InsertCitationsAsync(CitationSummary summary, IEnumerable<CitationSource> sources)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();

        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO CitationSummaries (Id, OrganizationId, TotalSources, AverageAuthorityScore, AverageInfluenceScore, HighestOpportunitySource, MostInfluentialSource, CreatedAt)
                VALUES (@Id, @OrganizationId, @TotalSources, @AverageAuthorityScore, @AverageInfluenceScore, @HighestOpportunitySource, @MostInfluentialSource, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var source in sources)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO CitationSources (Id, OrganizationId, Rank, Source, Category, AuthorityScore, InfluenceScore, CitationFrequency, CompetitorCoverage, OpportunityScore, MentionProbability, Reason, IsEnriched, EnrichedAt, CreatedAt)
                    VALUES (@Id, @OrganizationId, @Rank, @Source, @Category, @AuthorityScore, @InfluenceScore, @CitationFrequency, @CompetitorCoverage, @OpportunityScore, @MentionProbability, @Reason, @IsEnriched, @EnrichedAt, @CreatedAt)",
                    source, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<CitationSummary?> GetCitationSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // Return null if table doesn't exist
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'citationsummaries')"
        );
        if (!exists) return null;

        return await connection.QueryFirstOrDefaultAsync<CitationSummary>(
            "SELECT * FROM CitationSummaries WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<CitationSource>> GetCitationSourcesAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'citationsources')"
        );
        if (!exists) return new List<CitationSource>();

        return await connection.QueryAsync<CitationSource>(
            "SELECT * FROM CitationSources WHERE OrganizationId = @OrganizationId ORDER BY Rank ASC",
            new { OrganizationId = organizationId });
    }

    public async Task UpdateCitationSourcesAsync(IEnumerable<CitationSource> sources)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var source in sources)
            {
                await connection.ExecuteAsync(@"
                    UPDATE CitationSources 
                    SET AuthorityScore = @AuthorityScore,
                        InfluenceScore = @InfluenceScore,
                        CitationFrequency = @CitationFrequency,
                        CompetitorCoverage = @CompetitorCoverage,
                        OpportunityScore = @OpportunityScore,
                        MentionProbability = @MentionProbability,
                        Reason = @Reason,
                        IsEnriched = @IsEnriched,
                        EnrichedAt = @EnrichedAt
                    WHERE Id = @Id",
                    source, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<IEnumerable<CitationSource>> GetCitationsForEnrichmentAsync(Guid organizationId, int limit)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'citationsources')"
        );
        if (!exists) return new List<CitationSource>();

        return await connection.QueryAsync<CitationSource>(
            "SELECT * FROM CitationSources WHERE OrganizationId = @OrganizationId AND IsEnriched = FALSE ORDER BY Rank ASC LIMIT @Limit",
            new { OrganizationId = organizationId, Limit = limit });
    }

    public async Task UpdateCitationSummaryAsync(CitationSummary summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE CitationSummaries 
            SET AverageAuthorityScore = @AverageAuthorityScore,
                AverageInfluenceScore = @AverageInfluenceScore,
                HighestOpportunitySource = @HighestOpportunitySource,
                MostInfluentialSource = @MostInfluentialSource
            WHERE Id = @Id",
            summary);
    }

    public async Task InsertPersonaAnalysisAsync(PersonaAnalysisSummary summary, IEnumerable<PersonaScore> scores)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                INSERT INTO PersonaAnalysisSummaries (Id, OrganizationId, OverallVisibility, StrongestPersona, WeakestPersona, AverageShareOfVoice, CreatedAt)
                VALUES (@Id, @OrganizationId, @OverallVisibility, @StrongestPersona, @WeakestPersona, @AverageShareOfVoice, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var score in scores)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO PersonaScores (Id, OrganizationId, Persona, Visibility, AverageRank, ShareOfVoice, TopCompetitorsJson, RecommendedContentJson, Reason, CreatedAt)
                    VALUES (@Id, @OrganizationId, @Persona, @Visibility, @AverageRank, @ShareOfVoice, @TopCompetitorsJson, @RecommendedContentJson, @Reason, @CreatedAt)",
                    score, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<PersonaAnalysisSummary?> GetPersonaAnalysisSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'personaanalysissummaries')"
        );
        if (!exists) return null;

        return await connection.QueryFirstOrDefaultAsync<PersonaAnalysisSummary>(
            "SELECT * FROM PersonaAnalysisSummaries WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<PersonaScore>> GetPersonaScoresAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'personascores')"
        );
        if (!exists) return new List<PersonaScore>();

        return await connection.QueryAsync<PersonaScore>(
            "SELECT * FROM PersonaScores WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task InsertRegionAnalysisAsync(RegionAnalysisSummary summary, IEnumerable<RegionScore> scores)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                DELETE FROM RegionAnalysisSummaries WHERE OrganizationId = @OrganizationId;
                DELETE FROM RegionScores WHERE OrganizationId = @OrganizationId;",
                new { OrganizationId = summary.OrganizationId }, transaction);

            summary.Id = Guid.NewGuid();
            summary.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(@"
                INSERT INTO RegionAnalysisSummaries (Id, OrganizationId, OverallGlobalVisibility, StrongestRegion, WeakestRegion, AverageShareOfVoice, CreatedAt)
                VALUES (@Id, @OrganizationId, @OverallGlobalVisibility, @StrongestRegion, @WeakestRegion, @AverageShareOfVoice, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var score in scores)
            {
                score.Id = Guid.NewGuid();
                score.OrganizationId = summary.OrganizationId;
                score.CreatedAt = DateTime.UtcNow;
                await connection.ExecuteAsync(@"
                    INSERT INTO RegionScores (Id, OrganizationId, Region, Visibility, Ranking, CompetitorLeader, ShareOfVoice, ContentOpportunityJson, Reason, CreatedAt)
                    VALUES (@Id, @OrganizationId, @Region, @Visibility, @Ranking, @CompetitorLeader, @ShareOfVoice, @ContentOpportunityJson, @Reason, @CreatedAt)",
                    score, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<RegionAnalysisSummary?> GetRegionAnalysisSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'regionanalysissummaries')"
        );
        if (!exists) return null;

        return await connection.QueryFirstOrDefaultAsync<RegionAnalysisSummary>(
            "SELECT * FROM RegionAnalysisSummaries WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<RegionScore>> GetRegionScoresAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'regionscores')"
        );
        if (!exists) return new List<RegionScore>();

        return await connection.QueryAsync<RegionScore>(
            "SELECT * FROM RegionScores WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task InsertGeoRecommendationsAsync(GeoRecommendationSummary summary, IEnumerable<GeoRecommendation> recommendations)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                DELETE FROM GeoRecommendationSummaries WHERE OrganizationId = @OrganizationId;
                DELETE FROM GeoRecommendations WHERE OrganizationId = @OrganizationId;",
                new { OrganizationId = summary.OrganizationId }, transaction);

            summary.Id = Guid.NewGuid();
            summary.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(@"
                INSERT INTO GeoRecommendationSummaries (Id, OrganizationId, OverallPriority, EstimatedOverallImpact, EstimatedImplementationTime, TotalRecommendations, CriticalRecommendations, HighPriorityRecommendations, CreatedAt)
                VALUES (@Id, @OrganizationId, @OverallPriority, @EstimatedOverallImpact, @EstimatedImplementationTime, @TotalRecommendations, @CriticalRecommendations, @HighPriorityRecommendations, @CreatedAt)",
                summary, transaction: transaction);

            foreach (var rec in recommendations)
            {
                rec.Id = Guid.NewGuid();
                rec.OrganizationId = summary.OrganizationId;
                rec.CreatedAt = DateTime.UtcNow;
                await connection.ExecuteAsync(@"
                    INSERT INTO GeoRecommendations (Id, OrganizationId, RecommendationId, Category, Title, Description, Priority, EstimatedImpact, EstimatedDifficulty, ImplementationTime, ExpectedOutcome, SuccessMetric, ActionItemsJson, IsEnriched, EnrichedAt, ExpandedGuidance, BusinessImpact, ExampleResourcesJson, ReferenceLinksJson, CreatedAt)
                    VALUES (@Id, @OrganizationId, @RecommendationId, @Category, @Title, @Description, @Priority, @EstimatedImpact, @EstimatedDifficulty, @ImplementationTime, @ExpectedOutcome, @SuccessMetric, @ActionItemsJson, @IsEnriched, @EnrichedAt, @ExpandedGuidance, @BusinessImpact, @ExampleResourcesJson, @ReferenceLinksJson, @CreatedAt)",
                    rec, transaction: transaction);
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<GeoRecommendationSummary?> GetGeoRecommendationSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'georecommendationsummaries')"
        );
        if (!exists) return null;

        return await connection.QueryFirstOrDefaultAsync<GeoRecommendationSummary>(
            "SELECT * FROM GeoRecommendationSummaries WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<IEnumerable<GeoRecommendation>> GetGeoRecommendationsAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'georecommendations')"
        );
        if (!exists) return new List<GeoRecommendation>();

        return await connection.QueryAsync<GeoRecommendation>(
            "SELECT * FROM GeoRecommendations WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task UpdateGeoRecommendationAsync(GeoRecommendation recommendation)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(@"
            UPDATE GeoRecommendations
            SET IsEnriched = @IsEnriched,
                EnrichedAt = @EnrichedAt,
                ExpandedGuidance = @ExpandedGuidance,
                BusinessImpact = @BusinessImpact,
                ExampleResourcesJson = @ExampleResourcesJson,
                ReferenceLinksJson = @ReferenceLinksJson
            WHERE Id = @Id",
            recommendation);
    }

    public async Task<IEnumerable<GeoRecommendation>> GetGeoRecommendationsForEnrichmentAsync(Guid organizationId, int limit)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'georecommendations')"
        );
        if (!exists) return new List<GeoRecommendation>();

        return await connection.QueryAsync<GeoRecommendation>(
            "SELECT * FROM GeoRecommendations WHERE OrganizationId = @OrganizationId AND IsEnriched = FALSE ORDER BY CreatedAt DESC LIMIT @Limit",
            new { OrganizationId = organizationId, Limit = limit });
    }

    public async Task InsertExecutiveSummaryAsync(ExecutiveSummaryData summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(@"
                DELETE FROM ExecutiveSummaryData WHERE OrganizationId = @OrganizationId;",
                new { OrganizationId = summary.OrganizationId }, transaction);

            summary.Id = Guid.NewGuid();
            summary.CreatedAt = DateTime.UtcNow;
            await connection.ExecuteAsync(@"
                INSERT INTO ExecutiveSummaryData (Id, OrganizationId, BusinessOverview, CurrentAIVisibility, CompetitorPosition, PlatformPerformance, TopicPerformance, PromptPerformance, CitationSummary, StrengthsJson, WeaknessesJson, OpportunitiesJson, ThreatsJson, OverallGEOScore, OverallAIVisibilityScore, OverallSEOScore, OverallBrandAuthority, OverallContentScore, OverallAssessment, TopPriorityRecommendation, ExpectedBusinessImpact, NextStepsJson, CreatedAt)
                VALUES (@Id, @OrganizationId, @BusinessOverview, @CurrentAIVisibility, @CompetitorPosition, @PlatformPerformance, @TopicPerformance, @PromptPerformance, @CitationSummary, @StrengthsJson, @WeaknessesJson, @OpportunitiesJson, @ThreatsJson, @OverallGEOScore, @OverallAIVisibilityScore, @OverallSEOScore, @OverallBrandAuthority, @OverallContentScore, @OverallAssessment, @TopPriorityRecommendation, @ExpectedBusinessImpact, @NextStepsJson, @CreatedAt)",
                summary, transaction: transaction);

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<ExecutiveSummaryData?> GetExecutiveSummaryAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT FROM information_schema.tables WHERE table_name = 'executivesummarydata')"
        );
        if (!exists) return null;

        return await connection.QueryFirstOrDefaultAsync<ExecutiveSummaryData>(
            "SELECT * FROM ExecutiveSummaryData WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }
}
