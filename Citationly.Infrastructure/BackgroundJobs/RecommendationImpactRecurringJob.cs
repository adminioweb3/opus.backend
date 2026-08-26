using Citationly.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.BackgroundJobs;

public class RecommendationImpactRecurringJob
{
    private readonly IRecommendationImpactService _impactService;
    private readonly ILogger<RecommendationImpactRecurringJob> _logger;

    public RecommendationImpactRecurringJob(
        IRecommendationImpactService impactService,
        ILogger<RecommendationImpactRecurringJob> logger)
    {
        _impactService = impactService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var measured = await _impactService.ProcessDueMeasurementsAsync(organizationId: null, ct);
        _logger.LogInformation("RecommendationImpactRecurringJob: measured {Count} due recommendation implementation(s)", measured);
    }

    public Task RunAsync()
    {
        return RunAsync(CancellationToken.None);
    }
}
