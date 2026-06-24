using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface IIntegrationRepository
{
    Task<Guid> UpsertIntegrationAsync(Integration integration);
    Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId);
}
