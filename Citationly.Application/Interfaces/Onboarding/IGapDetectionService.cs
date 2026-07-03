using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Onboarding;

public class GapAnalysisResult
{
    public bool HasLowSearchVisibility { get; set; }
    public bool HasPoorBrandMentionRate { get; set; }
    public bool LacksCitations { get; set; }
    public bool HasPoorPersonaCoverage { get; set; }
    public bool HasPoorRegionalCoverage { get; set; }
    
    public List<string> WeakPlatforms { get; set; } = new();
    public List<string> MissingPersonas { get; set; } = new();
    public List<string> WeakRegions { get; set; } = new();

    public string GenerateSummaryString()
    {
        var summary = new List<string>();
        if (HasLowSearchVisibility) summary.Add("- Low overall search visibility across AI engines.");
        if (HasPoorBrandMentionRate) summary.Add("- Poor brand mention rate in AI responses.");
        if (LacksCitations) summary.Add("- Lacks authoritative citation sources.");
        if (HasPoorPersonaCoverage) summary.Add("- Incomplete persona coverage.");
        if (HasPoorRegionalCoverage) summary.Add("- Weak regional visibility.");
        
        if (WeakPlatforms.Any()) summary.Add($"- Weak performance on specific platforms: {string.Join(", ", WeakPlatforms)}");
        if (MissingPersonas.Any()) summary.Add($"- Missing critical personas: {string.Join(", ", MissingPersonas)}");
        if (WeakRegions.Any()) summary.Add($"- Weak regions: {string.Join(", ", WeakRegions)}");
        
        return string.Join("\n", summary);
    }
}

public interface IGapDetectionService
{
    Task<GapAnalysisResult> DetectGapsAsync(
        WebsiteProfile? profile,
        VisibilitySummary? visibility,
        IEnumerable<PlatformVisibility>? platforms,
        CitationSummary? citations,
        PersonaAnalysisSummary? personas,
        RegionAnalysisSummary? regions);
}
