using Citationly.Application.Features.Onboarding;
using Citationly.Application.Interfaces.Competitors;

namespace Citationly.Infrastructure.Services.Competitors;

public class CompetitorScoringEngine : ICompetitorScoringEngine
{
    public List<CompCompetitor> RankCompetitors(
        List<CompCompetitor> competitors,
        string sourceIndustry,
        string sourceTargetAudience,
        string sourceProducts,
        string sourceServices)
    {
        if (competitors == null || !competitors.Any()) return new List<CompCompetitor>();

        // Normalize source data for basic matching
        var indTarget = sourceIndustry?.ToLowerInvariant() ?? "";
        var audTarget = sourceTargetAudience?.ToLowerInvariant() ?? "";
        var prodTarget = sourceProducts?.ToLowerInvariant() ?? "";
        var servTarget = sourceServices?.ToLowerInvariant() ?? "";

        foreach (var comp in competitors)
        {
            int score = comp.similarityScore; // base score from AI

            // Weighting
            var compInd = comp.industry?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(indTarget) && compInd.Contains(indTarget)) score += 15;

            var compDesc = comp.description?.ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(audTarget) && compDesc.Contains(audTarget)) score += 10;
            if (!string.IsNullOrEmpty(prodTarget) && compDesc.Contains(prodTarget)) score += 10;
            if (!string.IsNullOrEmpty(servTarget) && compDesc.Contains(servTarget)) score += 10;

            // Type weighting
            var type = comp.competitorType?.ToLowerInvariant() ?? "";
            if (type.Contains("direct")) score += 20;
            else if (type.Contains("indirect")) score += 10;
            else if (type.Contains("emerging")) score += 5;

            // Cap at 100
            comp.similarityScore = Math.Min(100, Math.Max(0, score));
        }

        // Return ordered by similarity score descending
        return competitors.OrderByDescending(c => c.similarityScore).ToList();
    }
}
