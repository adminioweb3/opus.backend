using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class IntegrationRepository : IIntegrationRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IIntegrationCredentialProtector _credentialProtector;

    public IntegrationRepository(IDbConnectionFactory dbConnectionFactory, IIntegrationCredentialProtector credentialProtector)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _credentialProtector = credentialProtector;
    }

    public async Task<Guid> UpsertIntegrationAsync(Integration integration)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO Integrations (
                OrganizationId, PlatformName, ApiUrl, ApiKey, AuthType, Status,
                CredentialHint, LastVerifiedAt, LastError
            ) VALUES (
                @OrganizationId, @PlatformName, @ApiUrl, @ProtectedApiKey, @AuthType, 'Connected',
                @CredentialHint, CURRENT_TIMESTAMP, NULL
            )
            ON CONFLICT (OrganizationId, PlatformName) DO UPDATE SET
                ApiUrl = EXCLUDED.ApiUrl,
                ApiKey = EXCLUDED.ApiKey,
                AuthType = EXCLUDED.AuthType,
                Status = 'Connected',
                CredentialHint = EXCLUDED.CredentialHint,
                LastVerifiedAt = CURRENT_TIMESTAMP,
                LastError = NULL,
                UpdatedAt = CURRENT_TIMESTAMP
            RETURNING Id
            """,
            new
            {
                integration.OrganizationId,
                integration.PlatformName,
                integration.ApiUrl,
                ProtectedApiKey = _credentialProtector.Protect(integration.ApiKey ?? string.Empty),
                integration.AuthType,
                CredentialHint = BuildCredentialHint(integration.ApiKey)
            });
    }

    public async Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Integration>(
            """
            SELECT Id, OrganizationId, PlatformName, ApiUrl, AuthType, Status, CredentialHint,
                   LastVerifiedAt, LastError, CreatedAt, UpdatedAt
            FROM Integrations
            WHERE OrganizationId = @OrganizationId
            ORDER BY PlatformName
            """,
            new { OrganizationId = organizationId });
    }

    public Task<Integration?> GetIntegrationByOrgAndPlatformAsync(Guid organizationId, string platformName) =>
        GetServerIntegrationAsync("OrganizationId = @OrganizationId AND PlatformName = @PlatformName", new { OrganizationId = organizationId, PlatformName = platformName });

    public Task<Integration?> GetIntegrationByIdAsync(Guid id, Guid organizationId) =>
        GetServerIntegrationAsync("Id = @Id AND OrganizationId = @OrganizationId", new { Id = id, OrganizationId = organizationId });

    public async Task<bool> UpdateHealthAsync(Guid id, Guid organizationId, string status, string? lastError, DateTime verifiedAtUtc)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            """
            UPDATE Integrations
            SET Status = @Status, LastError = @LastError, LastVerifiedAt = @VerifiedAtUtc, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id AND OrganizationId = @OrganizationId
            """,
            new { Id = id, OrganizationId = organizationId, Status = status, LastError = lastError, VerifiedAtUtc = verifiedAtUtc }) == 1;
    }

    public async Task<bool> DeleteIntegrationAsync(Guid id, Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteAsync(
            "DELETE FROM Integrations WHERE Id = @Id AND OrganizationId = @OrganizationId",
            new { Id = id, OrganizationId = organizationId }) == 1;
    }

    private async Task<Integration?> GetServerIntegrationAsync(string whereClause, object parameters)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var integration = await connection.QuerySingleOrDefaultAsync<Integration>($"SELECT * FROM Integrations WHERE {whereClause} LIMIT 1", parameters);
        if (integration != null && !string.IsNullOrWhiteSpace(integration.ApiKey))
            integration.ApiKey = _credentialProtector.Unprotect(integration.ApiKey);
        return integration;
    }

    private static string BuildCredentialHint(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return string.Empty;
        var separator = apiKey.IndexOf(':');
        return separator > 0 ? $"{apiKey[..separator]}:****" : "****";
    }
}
