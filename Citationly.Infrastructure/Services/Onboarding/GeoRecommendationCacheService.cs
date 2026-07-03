using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Onboarding;

public class GeoRecommendationCacheService : IGeoRecommendationCacheService
{
    private readonly IWebsiteRepository _websiteRepository;

    public GeoRecommendationCacheService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<bool> IsCacheValidAsync(Guid organizationId, GeoRecommendationSummary existingSummary)
    {
        if (existingSummary == null) return false;

        // Fetch timestamps of dependent data to verify freshness
        var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(organizationId);
        var citationsSum = await _websiteRepository.GetCitationSummaryAsync(organizationId);
        var personasSum = await _websiteRepository.GetPersonaAnalysisSummaryAsync(organizationId);
        var regionsSum = await _websiteRepository.GetRegionAnalysisSummaryAsync(organizationId);

        // If any of the dependent summaries were generated AFTER the recommendations, the cache is invalid
        var recDate = existingSummary.CreatedAt;

        if (visibilitySum != null && visibilitySum.CreatedAt > recDate) return false;
        if (citationsSum != null && citationsSum.CreatedAt > recDate) return false;
        if (personasSum != null && personasSum.CreatedAt > recDate) return false;
        if (regionsSum != null && regionsSum.CreatedAt > recDate) return false;

        // Check if there are no recommendations actually generated
        if (existingSummary.TotalRecommendations == 0) return false;

        return true;
    }
}
