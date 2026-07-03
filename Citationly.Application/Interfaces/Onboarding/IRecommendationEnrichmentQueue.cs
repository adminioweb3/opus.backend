namespace Citationly.Application.Interfaces.Onboarding;

public interface IRecommendationEnrichmentQueue
{
    void EnqueueEnrichment(Guid organizationId);
}
