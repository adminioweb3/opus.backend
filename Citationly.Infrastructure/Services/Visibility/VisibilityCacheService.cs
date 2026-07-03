using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Visibility;

public class VisibilityCacheService : IVisibilityCacheService
{
    private readonly IWebsiteRepository _websiteRepository;

    public VisibilityCacheService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<VisibilitySummary?> GetCachedSummaryAsync(Guid organizationId)
    {
        return await _websiteRepository.GetVisibilitySummaryAsync(organizationId);
    }

    public async Task<List<PlatformVisibility>> GetCachedPlatformVisibilitiesAsync(Guid organizationId)
    {
        var visibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(organizationId);
        return visibilities.ToList();
    }
}
