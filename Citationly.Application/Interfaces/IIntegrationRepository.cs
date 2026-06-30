using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IIntegrationRepository
{
    Task<Guid> UpsertIntegrationAsync(Integration integration);
    Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId);
}
