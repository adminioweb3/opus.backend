using Dapper;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Infrastructure.Repositories;

public class EmbeddingRepository : IEmbeddingRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public EmbeddingRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> InsertEmbeddingAsync(Embedding embedding)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_InsertEmbedding(@OrganizationId, @ReferenceId, @ReferenceType, @TextContent, @Vector)",
            new
            {
                embedding.OrganizationId,
                embedding.ReferenceId,
                embedding.ReferenceType,
                embedding.TextContent,
                embedding.Vector
            });
    }

    public async Task<IEnumerable<Embedding>> GetEmbeddingsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Embedding>(
            "SELECT * FROM sp_GetEmbeddingsByOrg(@OrganizationId)",
            new { OrganizationId = organizationId });
    }
}
