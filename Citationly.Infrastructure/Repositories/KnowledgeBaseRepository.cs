using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class KnowledgeBaseRepository : IKnowledgeBaseRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public KnowledgeBaseRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<KnowledgeBase> CreateAsync(KnowledgeBase knowledgeBase)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO KnowledgeBases (OrganizationId, Name, Icon, Tint, Bg, Description)
              VALUES (@OrganizationId, @Name, @Icon, @Tint, @Bg, @Description)
              RETURNING Id",
            new
            {
                knowledgeBase.OrganizationId,
                knowledgeBase.Name,
                knowledgeBase.Icon,
                knowledgeBase.Tint,
                knowledgeBase.Bg,
                knowledgeBase.Description
            });

        knowledgeBase.Id = id;
        return knowledgeBase;
    }

    public async Task<IEnumerable<KnowledgeBase>> GetByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<KnowledgeBase>(
            "SELECT * FROM KnowledgeBases WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC",
            new { OrganizationId = organizationId });
    }

    public async Task<KnowledgeBase?> GetByIdAsync(Guid id)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<KnowledgeBase>(
            "SELECT * FROM KnowledgeBases WHERE Id = @Id",
            new { Id = id });
    }
}
