using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Visibility;

public interface IVisibilityScoringService
{
    List<PlatformVisibility> CalculatePlatformScores(Guid organizationId, List<AiSearchPrompt> prompts);
}
