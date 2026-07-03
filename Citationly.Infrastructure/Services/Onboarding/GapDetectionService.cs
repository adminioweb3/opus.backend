using Citationly.Application.Interfaces.Onboarding;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Onboarding;

public class GapDetectionService : IGapDetectionService
{
    public Task<GapAnalysisResult> DetectGapsAsync(
        WebsiteProfile? profile,
        VisibilitySummary? visibility,
        IEnumerable<PlatformVisibility>? platforms,
        CitationSummary? citations,
        PersonaAnalysisSummary? personas,
        RegionAnalysisSummary? regions)
    {
        var result = new GapAnalysisResult();

        // Evaluate Visibility
        if (visibility != null)
        {
            if (visibility.OverallVisibilityScore < 50) result.HasLowSearchVisibility = true;
            if (visibility.AverageMentionRate < 0.3) result.HasPoorBrandMentionRate = true;
        }

        // Evaluate Platforms
        if (platforms != null)
        {
            var weakPlatforms = platforms.Where(p => p.VisibilityScore < 40).Select(p => p.Platform).ToList();
            result.WeakPlatforms.AddRange(weakPlatforms);
        }

        // Evaluate Citations
        if (citations != null)
        {
            if (citations.AverageAuthorityScore < 40 || citations.TotalMentionsAnalyzed < 5) 
                result.LacksCitations = true;
        }

        // Evaluate Personas
        if (personas != null)
        {
            if (personas.OverallVisibility < 50) result.HasPoorPersonaCoverage = true;
            // Optionally, we could look at individual persona scores if passed, 
            // but for now relying on the summary.
        }

        // Evaluate Regions
        if (regions != null)
        {
            if (regions.OverallGlobalVisibility < 50) result.HasPoorRegionalCoverage = true;
        }

        return Task.FromResult(result);
    }
}
