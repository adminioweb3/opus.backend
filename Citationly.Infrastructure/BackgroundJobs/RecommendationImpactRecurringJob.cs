using Citationly.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.BackgroundJobs;

public class RecommendationImpactRecurringJob
{
    private readonly IRecommendationImpactService _impactService;
    private readonly IAgentAutomationService _agentAutomation;
    private readonly ILogger<RecommendationImpactRecurringJob> _logger;

    public RecommendationImpactRecurringJob(
        IRecommendationImpactService impactService,
        IAgentAutomationService agentAutomation,
        ILogger<RecommendationImpactRecurringJob> logger)
    {
        _impactService = impactService;
        _agentAutomation = agentAutomation;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var measured = await _impactService.ProcessDueMeasurementsAsync(organizationId: null, ct);
        var agentMeasured = await _agentAutomation.ProcessDueImpactMeasurementsAsync(organizationId: null, ct);
        _logger.LogInformation(
            "RecommendationImpactRecurringJob: measured {PromptCount} prompt recommendation(s) and {AgentCount} agent recommendation(s)",
            measured,
            agentMeasured);
    }

    public Task RunAsync()
    {
        return RunAsync(CancellationToken.None);
    }
}
