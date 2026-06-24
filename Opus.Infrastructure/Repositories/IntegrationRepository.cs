using Dapper;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Infrastructure.Repositories;

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
}
