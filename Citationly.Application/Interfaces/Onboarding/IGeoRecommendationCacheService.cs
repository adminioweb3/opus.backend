using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Onboarding;

public interface IGeoRecommendationCacheService
{
    /// <summary>
    /// Validates if the existing recommendations for the organization are still fresh based on
    /// the creation time of other reports (prompts, personas, citations, etc.).
    /// </summary>
    Task<bool> IsCacheValidAsync(Guid organizationId, GeoRecommendationSummary existingSummary);
}
