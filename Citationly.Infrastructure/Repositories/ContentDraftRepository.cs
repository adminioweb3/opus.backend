using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class ContentDraftRepository : IContentDraftRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ContentDraftRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<ContentDraft> CreateAsync(ContentDraft draft)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO ContentDrafts (OrganizationId, Title, ContentType, Content, WordCount, Status, RequestJson, CompetitorUrl)
              VALUES (@OrganizationId, @Title, @ContentType, @Content, @WordCount, @Status, @RequestJson::jsonb, @CompetitorUrl)
              RETURNING Id",
            new
            {
                draft.OrganizationId,
                draft.Title,
                draft.ContentType,
                draft.Content,
                draft.WordCount,
                draft.Status,
                draft.RequestJson,
                draft.CompetitorUrl
            });

        draft.Id = id;
        return draft;
    }

    public async Task<ContentDraft?> GetByIdAsync(Guid id)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ContentDraft>(
            "SELECT * FROM ContentDrafts WHERE Id = @Id",
            new { Id = id });
    }

    public async Task<List<ContentDraft>> GetByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<ContentDraft>(
            "SELECT * FROM ContentDrafts WHERE OrganizationId = @OrganizationId ORDER BY UpdatedAt DESC",
            new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task UpdateAsync(ContentDraft draft)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"UPDATE ContentDrafts
              SET Title = @Title,
                  Content = @Content,
                  WordCount = @WordCount,
                  Status = @Status,
                  PublishedUrl = @PublishedUrl,
                  PublishedAt = @PublishedAt,
                  IntegrationId = @IntegrationId,
                  PublishError = @PublishError,
                  UpdatedAt = NOW()
              WHERE Id = @Id",
            new
            {
                draft.Id, draft.Title, draft.Content, draft.WordCount, draft.Status,
                draft.PublishedUrl, draft.PublishedAt, draft.IntegrationId, draft.PublishError
            });
    }
}
