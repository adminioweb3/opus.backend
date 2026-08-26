using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class SsoRepository : ISsoRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public SsoRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<SsoConnection?> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<SsoConnection>(
            "SELECT * FROM SsoConnections WHERE OrganizationId = @OrganizationId ORDER BY UpdatedAt DESC LIMIT 1",
            new { OrganizationId = organizationId });
    }

    public async Task<SsoConnection> UpsertAsync(SsoConnection connection, CancellationToken cancellationToken = default)
    {
        using var db = _dbConnectionFactory.CreateConnection();
        return await db.QuerySingleAsync<SsoConnection>(
            """
            INSERT INTO SsoConnections (OrganizationId, Provider, Domain, MetadataUrl, EntityId, IsEnabled, UpdatedAt)
            VALUES (@OrganizationId, @Provider, @Domain, @MetadataUrl, @EntityId, @IsEnabled, CURRENT_TIMESTAMP)
            ON CONFLICT (OrganizationId, Provider, Domain) DO UPDATE SET
                MetadataUrl = EXCLUDED.MetadataUrl,
                EntityId = EXCLUDED.EntityId,
                IsEnabled = EXCLUDED.IsEnabled,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING *
            """,
            connection);
    }

    public async Task<SsoConnection?> GetByScimTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<SsoConnection>(
            "SELECT * FROM SsoConnections WHERE ScimEnabled = TRUE AND ScimTokenHash = @TokenHash LIMIT 1",
            new { TokenHash = tokenHash });
    }

    public async Task SetScimTokenHashAsync(Guid organizationId, string tokenHash, CancellationToken cancellationToken = default)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            """
            UPDATE SsoConnections
            SET ScimEnabled = TRUE, ScimTokenHash = @TokenHash, UpdatedAt = CURRENT_TIMESTAMP
            WHERE OrganizationId = @OrganizationId
            """,
            new { OrganizationId = organizationId, TokenHash = tokenHash });
    }
}
