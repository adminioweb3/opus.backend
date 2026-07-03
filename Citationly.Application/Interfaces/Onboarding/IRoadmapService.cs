using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Onboarding;

public class RoadmapGenerationResult
{
    public GeoRecommendationSummary Summary { get; set; } = new();
    public List<GeoRecommendation> Recommendations { get; set; } = new();
}

public interface IRoadmapService
{
    /// <summary>
    /// Deterministically generates a roadmap (assigning priorities, impact, timeframes, etc.)
    /// from a raw list of discovered recommendations and gap analysis.
    /// </summary>
    RoadmapGenerationResult GenerateRoadmap(
        Guid organizationId, 
        List<DiscoveryRecommendationDto> discoveredRecs, 
        GapAnalysisResult gapAnalysis);
}
