using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Visibility;

public interface IVisibilityCacheService
{
    Task<VisibilitySummary?> GetCachedSummaryAsync(Guid organizationId);
    Task<List<PlatformVisibility>> GetCachedPlatformVisibilitiesAsync(Guid organizationId);
}
