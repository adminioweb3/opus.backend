namespace Citationly.Application.Interfaces.Onboarding;

public class DiscoveryRecommendationDto
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public interface IRecommendationDiscoveryService
{
    /// <summary>
    /// Uses AI to discover recommendations based on the identified gaps.
    /// Returns a lightweight DTO containing only Category, Title, and Description.
    /// </summary>
    Task<List<DiscoveryRecommendationDto>> DiscoverRecommendationsAsync(GapAnalysisResult gapAnalysis, string websiteUrl, string rawProfileJson);
}
