using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IAgencyRepository
{
    Task<Agency> CreateOrGetAgencyAsync(Guid ownerOrganizationId, string name);
    Task<Agency?> GetAgencyByOwnerOrgAsync(Guid ownerOrganizationId);
    Task<IEnumerable<AgencyClient>> GetClientsAsync(Guid agencyId);
    Task<AgencyClient?> AddClientAsync(Guid agencyId, Guid clientOrganizationId, string clientName, string role);
    Task UpsertWhiteLabelSettingsAsync(WhiteLabelSettings settings);
    Task<WhiteLabelSettings?> GetWhiteLabelSettingsAsync(Guid agencyId);
    Task<Guid> CreateReportShareLinkAsync(ReportShareLink link);
    Task<ReportShareLink?> GetReportShareLinkByTokenHashAsync(string tokenHash);
}
