using Citationly.Application.Interfaces.Onboarding;
using Hangfire;

namespace Citationly.Infrastructure.Services.Onboarding;

public class RecommendationEnrichmentQueue : IRecommendationEnrichmentQueue
{
    private readonly IBackgroundJobClient _backgroundJobClient;

    public RecommendationEnrichmentQueue(IBackgroundJobClient backgroundJobClient)
    {
        _backgroundJobClient = backgroundJobClient;
    }

    public void EnqueueEnrichment(Guid organizationId)
    {
        _backgroundJobClient.Enqueue<RecommendationBackgroundWorker>(
            worker => worker.EnrichRecommendationsAsync(organizationId));
    }
}
