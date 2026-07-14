using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.GeoDashboard;

namespace Citationly.Application.Features.GeoDashboard;

public class EngineScanService : IEngineScanService
{
    private readonly IWebsiteRepository _websiteRepository;

    public EngineScanService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<(int EnginesScanned, int PromptsTracked)> GetScanStatsAsync(Guid organizationId)
    {
        var platformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(organizationId);
        var enginesScanned = platformVisibilities.Select(p => p.Platform).Distinct().Count();

        var promptsTracked = await _websiteRepository.GetAiSearchPromptCountAsync(organizationId);

        return (enginesScanned, promptsTracked);
    }
}
