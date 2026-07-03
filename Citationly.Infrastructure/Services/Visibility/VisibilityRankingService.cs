using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Visibility;

public class VisibilityRankingService : IVisibilityRankingService
{
    public VisibilitySummary CalculateOverallSummary(Guid organizationId, List<PlatformVisibility> platformScores)
    {
        if (platformScores == null || !platformScores.Any())
        {
            return new VisibilitySummary
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                OverallVisibilityScore = 0,
                BestPlatform = "None",
                WeakestPlatform = "None",
                AverageMentionRate = 0,
                AveragePromptCoverage = 0,
                CreatedAt = DateTime.UtcNow
            };
        }

        var overallScore = (int)platformScores.Average(p => p.VisibilityScore);
        var averageMentionRate = (int)platformScores.Average(p => p.MentionRate);
        var averagePromptCoverage = (int)platformScores.Average(p => p.PromptCoverage);

        var bestPlatform = platformScores.OrderByDescending(p => p.VisibilityScore).First().Platform;
        var weakestPlatform = platformScores.OrderBy(p => p.VisibilityScore).First().Platform;

        return new VisibilitySummary
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            OverallVisibilityScore = overallScore,
            BestPlatform = bestPlatform,
            WeakestPlatform = weakestPlatform,
            AverageMentionRate = averageMentionRate,
            AveragePromptCoverage = averagePromptCoverage,
            CreatedAt = DateTime.UtcNow
        };
    }
}
