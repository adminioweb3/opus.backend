using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class SourceFolderRepository : ISourceFolderRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SourceFolderRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<SourceFolder> CreateAsync(SourceFolder folder)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var id = await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO SourceFolders (KnowledgeBaseId, Name)
              VALUES (@KnowledgeBaseId, @Name)
              RETURNING Id",
            new { folder.KnowledgeBaseId, folder.Name });

        folder.Id = id;
        return folder;
    }

    public async Task<IEnumerable<SourceFolder>> GetByKnowledgeBaseAsync(Guid knowledgeBaseId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<SourceFolder>(
            "SELECT * FROM SourceFolders WHERE KnowledgeBaseId = @KnowledgeBaseId ORDER BY CreatedAt DESC",
            new { KnowledgeBaseId = knowledgeBaseId });
    }
}
