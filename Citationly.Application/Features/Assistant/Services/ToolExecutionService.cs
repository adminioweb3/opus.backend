using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Assistant.Services;

public class ToolExecutionService
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IMetricsRepository _metricsRepository;

    public ToolExecutionService(IWebsiteRepository websiteRepository, IMetricsRepository metricsRepository)
    {
        _websiteRepository = websiteRepository;
        _metricsRepository = metricsRepository;
    }

    public async Task<Dictionary<string, object>> ExecuteToolsAsync(Guid? organizationId, string[] requiredTools, CancellationToken ct)
    {
        var rawData = new Dictionary<string, object>();
        
        if (!organizationId.HasValue) 
            return rawData;

        // Base data - always load websites
        var websites = await _websiteRepository.GetWebsitesByOrgAsync(organizationId.Value);
        rawData["websites"] = websites.Select(w => new { w.DomainUrl, w.HealthScore, w.VisibilityScore, w.PlatformName }).ToList();

        // If intent detection failed or no tools required, fallback to loading core datasets so the AI has context
        bool loadCompetitors = requiredTools.Contains("Competitor Tool") || requiredTools.Length == 0;
        bool loadVisibility = requiredTools.Contains("Visibility Tool") || requiredTools.Length == 0;

        if (loadCompetitors)
        {
            var shareOfVoices = await _metricsRepository.GetShareOfVoiceAsync(organizationId.Value, DateTime.UtcNow.Date);
            rawData["competitors"] = shareOfVoices.Select(s => new { s.CompetitorName, s.SharePercentage }).ToList();
        }

        if (loadVisibility)
        {
            var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(organizationId.Value);
            if (visibilitySum != null)
            {
                rawData["visibilitySummary"] = new { visibilitySum.OverallVisibilityScore, visibilitySum.BestPlatform, visibilitySum.WeakestPlatform, visibilitySum.AverageMentionRate };
            }

            var platVis = await _websiteRepository.GetPlatformVisibilitiesAsync(organizationId.Value);
            if (platVis != null && platVis.Any())
            {
                rawData["platformVisibilities"] = platVis.Select(p => new { p.Platform, p.VisibilityScore, p.MentionRate, p.PromptCoverage }).ToList();
            }
        }

        // Add more tools as needed (e.g., calling external SEO APIs)
        
        return rawData;
    }
}
