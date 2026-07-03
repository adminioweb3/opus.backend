using Citationly.Application.Features.Onboarding;

namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorScoringEngine
{
    /// <summary>
    /// Ranks a list of competitors based on weighted business intelligence similarity.
    /// </summary>
    List<CompCompetitor> RankCompetitors(
        List<CompCompetitor> competitors,
        string sourceIndustry,
        string sourceTargetAudience,
        string sourceProducts,
        string sourceServices);
}
