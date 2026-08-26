using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ApiKeyRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<IEnumerable<ApiKey>> GetApiKeysByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<ApiKey>(
            """
            SELECT Id, OrganizationId, Name, KeyPrefix, KeyHash, Last4, CreatedAt, RevokedAt
            FROM ApiKeys
            WHERE OrganizationId = @OrganizationId
            ORDER BY CreatedAt DESC
            """,
            new { OrganizationId = organizationId });
    }

    public async Task<Guid> CreateApiKeyAsync(ApiKey apiKey)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO ApiKeys (OrganizationId, Name, KeyPrefix, KeyHash, Last4)
            VALUES (@OrganizationId, @Name, @KeyPrefix, @KeyHash, @Last4)
            RETURNING Id
            """,
            apiKey);
    }

    public async Task<ApiKey?> GetActiveApiKeyByHashAsync(string keyHash)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ApiKey>(
            """
            SELECT Id, OrganizationId, Name, KeyPrefix, KeyHash, Last4, CreatedAt, RevokedAt
            FROM ApiKeys
            WHERE KeyHash = @KeyHash AND RevokedAt IS NULL
            LIMIT 1
            """,
            new { KeyHash = keyHash });
    }

    public async Task<bool> RevokeApiKeyAsync(Guid id, Guid organizationId, DateTime revokedAtUtc)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            """
            UPDATE ApiKeys
            SET RevokedAt = @RevokedAt
            WHERE Id = @Id AND OrganizationId = @OrganizationId AND RevokedAt IS NULL
            """,
            new { Id = id, OrganizationId = organizationId, RevokedAt = revokedAtUtc });
        return rows > 0;
    }
}
