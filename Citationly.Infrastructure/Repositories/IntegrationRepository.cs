using Dapper;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Repositories;

public class IntegrationRepository : IIntegrationRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public IntegrationRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> UpsertIntegrationAsync(Integration integration)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            "SELECT sp_UpsertIntegration(@OrganizationId, @PlatformName, @ApiUrl, @ApiKey)",
            new
            {
                integration.OrganizationId,
                integration.PlatformName,
                integration.ApiUrl,
                integration.ApiKey
            });
    }

    public async Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<Integration>(
            "SELECT * FROM sp_GetIntegrationsByOrg(@OrganizationId)",
            new { OrganizationId = organizationId });
    }

    public async Task<Integration?> GetIntegrationByOrgAndPlatformAsync(Guid organizationId, string platformName)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Integration>(
            "SELECT * FROM Integrations WHERE OrganizationId = @OrganizationId AND PlatformName = @PlatformName LIMIT 1",
            new { OrganizationId = organizationId, PlatformName = platformName });
    }
}
