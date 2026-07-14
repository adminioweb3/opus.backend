using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class ContentOptimizationRepository : IContentOptimizationRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ContentOptimizationRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<ContentOptimization> CreateAsync(ContentOptimization optimization)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO ContentOptimizations
                (ContentDraftId, OrganizationId, SeoScore, ReadabilityScore, HumanizedScore, AiScore,
                 KeywordDensity, PrimaryKeyword, RecommendationsJson, InternalLinksJson, CitationRecsJson, OptimizedContent)
              VALUES
                (@ContentDraftId, @OrganizationId, @SeoScore, @ReadabilityScore, @HumanizedScore, @AiScore,
                 @KeywordDensity, @PrimaryKeyword, @RecommendationsJson::jsonb, @InternalLinksJson::jsonb, @CitationRecsJson::jsonb, @OptimizedContent)
              RETURNING Id",
            optimization);

        optimization.Id = id;
        return optimization;
    }

    public async Task<ContentOptimization?> GetLatestByDraftAsync(Guid contentDraftId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ContentOptimization>(
            "SELECT * FROM ContentOptimizations WHERE ContentDraftId = @ContentDraftId ORDER BY CreatedAt DESC LIMIT 1",
            new { ContentDraftId = contentDraftId });
    }
}
