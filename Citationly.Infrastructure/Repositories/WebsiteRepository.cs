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

    public async Task<Guid> InsertWebsiteProfileAsync(WebsiteProfile profile)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        // Ensure table exists (temporary migration pattern)
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS WebsiteProfiles (
                Id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                OrganizationId UUID NOT NULL,
                WebsiteUrl TEXT NOT NULL,
                BusinessName TEXT NOT NULL,
                RawProfileJson JSONB NOT NULL,
                CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
        ");

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

        await connection.ExecuteAsync(@"
            ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS Rank INTEGER DEFAULT 0;
            ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS SimilarityScore INTEGER DEFAULT 0;
            ALTER TABLE Competitors ADD COLUMN IF NOT EXISTS RawJson JSONB DEFAULT '{}'::jsonb;
        ");

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var comp in competitors)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO Competitors (OrganizationId, Name, WebsiteUrl, Industry, Description, Category, Logo, Country, Authority, Popularity, Rank, SimilarityScore, RawJson, CreatedAt)
                    VALUES (@OrganizationId, @Name, @WebsiteUrl, @Industry, @Description, @Category, @Logo, @Country, @Authority, @Popularity, @Rank, @SimilarityScore, @RawJson::jsonb, @CreatedAt)",
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
        // Return count of competitors for the given organization
        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM Competitors WHERE OrganizationId = @OrganizationId",
            new { OrganizationId = organizationId });
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

        // Auto-migrate schema for the new JSON properties
        await connection.ExecuteAsync(@"
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Topic VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Intent VARCHAR(100);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Difficulty VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Persona VARCHAR(255);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CommercialValue INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS RawJson JSONB DEFAULT '{}'::jsonb;
        ");

        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var prompt in prompts)
            {
                await connection.ExecuteAsync(@"
                    INSERT INTO AiSearchPrompts (OrganizationId, QueryString, SearchEngine, Topic, Intent, Difficulty, Persona, CommercialValue, RawJson, GeneratedAt)
                    VALUES (@OrganizationId, @QueryString, @SearchEngine, @Topic, @Intent, @Difficulty, @Persona, @CommercialValue, @RawJson::jsonb, @GeneratedAt)",
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

        // Auto-migrate schema for the new visibility properties
        await connection.ExecuteAsync(@"
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityScore INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS EstimatedRank VARCHAR(50);
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS Confidence INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS AppearsInAnswer BOOLEAN DEFAULT FALSE;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ShareOfVoiceContribution INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS MentionProbability INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS BrandStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS ContentStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS CitationStrength INTEGER DEFAULT 0;
            ALTER TABLE AiSearchPrompts ADD COLUMN IF NOT EXISTS VisibilityReason TEXT;
        ");

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

    public async Task InsertPlatformVisibilityAsync(VisibilitySummary summary, IEnumerable<PlatformVisibility> visibilities)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS VisibilitySummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                OverallVisibilityScore INTEGER NOT NULL,
                BestPlatform VARCHAR(255),
                WeakestPlatform VARCHAR(255),
                AverageMentionRate INTEGER NOT NULL,
                AveragePromptCoverage INTEGER NOT NULL,
                CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PlatformVisibilities (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                Platform VARCHAR(255) NOT NULL,
                VisibilityScore INTEGER NOT NULL,
                AverageRank VARCHAR(50),
                MentionRate INTEGER NOT NULL,
                PromptCoverage INTEGER NOT NULL,
                Confidence INTEGER NOT NULL,
                StrengthsJson TEXT,
                WeaknessesJson TEXT,
                CreatedAt TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
            );
        ");

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

        // Auto-migrate schema
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS CitationSummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                TotalSources INTEGER NOT NULL,
                AverageAuthorityScore INTEGER NOT NULL,
                AverageInfluenceScore INTEGER NOT NULL,
                HighestOpportunitySource VARCHAR(255),
                MostInfluentialSource VARCHAR(255),
                CreatedAt TIMESTAMP NOT NULL
            );
            CREATE TABLE IF NOT EXISTS CitationSources (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                Rank INTEGER NOT NULL,
                Source VARCHAR(255) NOT NULL,
                Category VARCHAR(255),
                AuthorityScore INTEGER NOT NULL,
                InfluenceScore INTEGER NOT NULL,
                CitationFrequency INTEGER NOT NULL,
                CompetitorCoverage INTEGER NOT NULL,
                OpportunityScore INTEGER NOT NULL,
                MentionProbability INTEGER NOT NULL,
                Reason TEXT,
                CreatedAt TIMESTAMP NOT NULL
            );
        ");

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
                    INSERT INTO CitationSources (Id, OrganizationId, Rank, Source, Category, AuthorityScore, InfluenceScore, CitationFrequency, CompetitorCoverage, OpportunityScore, MentionProbability, Reason, CreatedAt)
                    VALUES (@Id, @OrganizationId, @Rank, @Source, @Category, @AuthorityScore, @InfluenceScore, @CitationFrequency, @CompetitorCoverage, @OpportunityScore, @MentionProbability, @Reason, @CreatedAt)",
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

    public async Task InsertPersonaAnalysisAsync(PersonaAnalysisSummary summary, IEnumerable<PersonaScore> scores)
    {
        using var connection = _dbConnectionFactory.CreateConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS PersonaAnalysisSummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                OverallVisibility INTEGER NOT NULL,
                StrongestPersona VARCHAR(255),
                WeakestPersona VARCHAR(255),
                AverageShareOfVoice INTEGER NOT NULL,
                CreatedAt TIMESTAMP NOT NULL
            );
            CREATE TABLE IF NOT EXISTS PersonaScores (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                Persona VARCHAR(255) NOT NULL,
                Visibility INTEGER NOT NULL,
                AverageRank VARCHAR(50),
                ShareOfVoice INTEGER NOT NULL,
                TopCompetitorsJson TEXT,
                RecommendedContentJson TEXT,
                Reason TEXT,
                CreatedAt TIMESTAMP NOT NULL
            );
        ");

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
        
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS RegionAnalysisSummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                OverallGlobalVisibility INT NOT NULL,
                StrongestRegion TEXT,
                WeakestRegion TEXT,
                AverageShareOfVoice INT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL
            )");
            
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS RegionScores (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                Region TEXT NOT NULL,
                Visibility INT NOT NULL,
                Ranking TEXT,
                CompetitorLeader TEXT,
                ShareOfVoice INT NOT NULL,
                ContentOpportunityJson TEXT,
                Reason TEXT,
                CreatedAt TIMESTAMP NOT NULL
            )");

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
        
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS GeoRecommendationSummaries (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                OverallPriority TEXT,
                EstimatedOverallImpact TEXT,
                EstimatedImplementationTime TEXT,
                TotalRecommendations INT NOT NULL,
                CriticalRecommendations INT NOT NULL,
                HighPriorityRecommendations INT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL
            )");
            
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS GeoRecommendations (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                RecommendationId TEXT NOT NULL,
                Category TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                Priority TEXT NOT NULL,
                EstimatedImpact TEXT NOT NULL,
                EstimatedDifficulty TEXT NOT NULL,
                ImplementationTime TEXT NOT NULL,
                ExpectedOutcome TEXT NOT NULL,
                SuccessMetric TEXT NOT NULL,
                ActionItemsJson TEXT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL
            )");

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
                    INSERT INTO GeoRecommendations (Id, OrganizationId, RecommendationId, Category, Title, Description, Priority, EstimatedImpact, EstimatedDifficulty, ImplementationTime, ExpectedOutcome, SuccessMetric, ActionItemsJson, CreatedAt)
                    VALUES (@Id, @OrganizationId, @RecommendationId, @Category, @Title, @Description, @Priority, @EstimatedImpact, @EstimatedDifficulty, @ImplementationTime, @ExpectedOutcome, @SuccessMetric, @ActionItemsJson, @CreatedAt)",
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

    public async Task InsertExecutiveSummaryAsync(ExecutiveSummaryData summary)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ExecutiveSummaryData (
                Id UUID PRIMARY KEY,
                OrganizationId UUID NOT NULL,
                BusinessOverview TEXT NOT NULL,
                CurrentAIVisibility TEXT NOT NULL,
                CompetitorPosition TEXT NOT NULL,
                PlatformPerformance TEXT NOT NULL,
                TopicPerformance TEXT NOT NULL,
                PromptPerformance TEXT NOT NULL,
                CitationSummary TEXT NOT NULL,
                StrengthsJson TEXT NOT NULL,
                WeaknessesJson TEXT NOT NULL,
                OpportunitiesJson TEXT NOT NULL,
                ThreatsJson TEXT NOT NULL,
                OverallGEOScore INT NOT NULL,
                OverallAIVisibilityScore INT NOT NULL,
                OverallSEOScore INT NOT NULL,
                OverallBrandAuthority INT NOT NULL,
                OverallContentScore INT NOT NULL,
                OverallAssessment TEXT NOT NULL,
                TopPriorityRecommendation TEXT NOT NULL,
                ExpectedBusinessImpact TEXT NOT NULL,
                NextStepsJson TEXT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL
            )");

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
