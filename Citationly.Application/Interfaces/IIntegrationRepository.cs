using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IIntegrationRepository
{
    Task<Guid> UpsertIntegrationAsync(Integration integration);
    Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId);

    /// <summary>
    /// Server-side only: includes the real ApiKey, unlike <see cref="GetIntegrationsByOrgAsync"/>
    /// which intentionally omits it. Never expose this result directly to a client response.
    /// </summary>
    Task<Integration?> GetIntegrationByOrgAndPlatformAsync(Guid organizationId, string platformName);
}
