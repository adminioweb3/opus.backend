using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class AgencyRepository : IAgencyRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public AgencyRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Agency> CreateOrGetAgencyAsync(Guid ownerOrganizationId, string name)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleAsync<Agency>(
            @"INSERT INTO Agencies (OwnerOrganizationId, Name)
              VALUES (@OwnerOrganizationId, @Name)
              ON CONFLICT (OwnerOrganizationId) DO UPDATE SET Name = COALESCE(NULLIF(@Name, ''), Agencies.Name)
              RETURNING *",
            new { OwnerOrganizationId = ownerOrganizationId, Name = name });
    }

    public async Task<Agency?> GetAgencyByOwnerOrgAsync(Guid ownerOrganizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Agency>(
            "SELECT * FROM Agencies WHERE OwnerOrganizationId = @OwnerOrganizationId",
            new { OwnerOrganizationId = ownerOrganizationId });
    }

    public async Task<IEnumerable<AgencyClient>> GetClientsAsync(Guid agencyId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryAsync<AgencyClient>(
            "SELECT * FROM AgencyClients WHERE AgencyId = @AgencyId ORDER BY CreatedAt DESC",
            new { AgencyId = agencyId });
    }

    public async Task<AgencyClient?> AddClientAsync(Guid agencyId, Guid clientOrganizationId, string clientName, string role)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<AgencyClient>(
            @"INSERT INTO AgencyClients (AgencyId, ClientOrganizationId, ClientName, Role)
              VALUES (@AgencyId, @ClientOrganizationId, @ClientName, @Role)
              ON CONFLICT (AgencyId, ClientOrganizationId) DO UPDATE SET
                ClientName = EXCLUDED.ClientName,
                Role = EXCLUDED.Role
              RETURNING *",
            new { AgencyId = agencyId, ClientOrganizationId = clientOrganizationId, ClientName = clientName, Role = role });
    }

    public async Task UpsertWhiteLabelSettingsAsync(WhiteLabelSettings settings)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            @"INSERT INTO WhiteLabelSettings (AgencyId, BrandName, LogoUrl, PrimaryColor, UpdatedAt)
              VALUES (@AgencyId, @BrandName, @LogoUrl, @PrimaryColor, CURRENT_TIMESTAMP)
              ON CONFLICT (AgencyId) DO UPDATE SET
                BrandName = EXCLUDED.BrandName,
                LogoUrl = EXCLUDED.LogoUrl,
                PrimaryColor = EXCLUDED.PrimaryColor,
                UpdatedAt = CURRENT_TIMESTAMP",
            settings);
    }

    public async Task<WhiteLabelSettings?> GetWhiteLabelSettingsAsync(Guid agencyId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<WhiteLabelSettings>(
            "SELECT * FROM WhiteLabelSettings WHERE AgencyId = @AgencyId",
            new { AgencyId = agencyId });
    }

    public async Task<Guid> CreateReportShareLinkAsync(ReportShareLink link)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<Guid>(
            @"INSERT INTO ReportShareLinks (OrganizationId, AgencyId, TokenHash, ReportType, ExpiresAt)
              VALUES (@OrganizationId, @AgencyId, @TokenHash, @ReportType, @ExpiresAt)
              RETURNING Id",
            link);
    }

    public async Task<ReportShareLink?> GetReportShareLinkByTokenHashAsync(string tokenHash)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ReportShareLink>(
            "SELECT * FROM ReportShareLinks WHERE TokenHash = @TokenHash AND ExpiresAt > CURRENT_TIMESTAMP AND RevokedAt IS NULL",
            new { TokenHash = tokenHash });
    }

    public async Task<bool> RevokeReportShareLinkAsync(Guid id, Guid agencyId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var rows = await connection.ExecuteAsync(
            @"UPDATE ReportShareLinks
              SET RevokedAt = CURRENT_TIMESTAMP
              WHERE Id = @Id AND AgencyId = @AgencyId AND RevokedAt IS NULL;",
            new { Id = id, AgencyId = agencyId });
        return rows > 0;
    }
}
