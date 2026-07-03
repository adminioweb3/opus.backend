using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Visibility;

public interface IVisibilityRankingService
{
    VisibilitySummary CalculateOverallSummary(Guid organizationId, List<PlatformVisibility> platformScores);
}
