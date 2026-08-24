using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.CommandCenter;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only regenerates an organization's Command Center business
/// insights once its plan's configured interval (PlanLimits "recurring_scan_interval_days",
/// default 7 if unset) has passed since its last CommandCenterInsightSnapshot. Processes
/// organizations with bounded concurrency rather than one giant sequential loop, per the
/// audit's cost-scale finding.
/// </summary>
public class CommandCenterInsightsRecurringJob
{
    private const int DefaultScanIntervalDays = 7;
    private const int MaxConcurrency = 5;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICommandCenterInsightRepository _snapshotRepo;
    private readonly IEntitlementService _entitlements;
    private readonly IMediator _mediator;
    private readonly ILogger<CommandCenterInsightsRecurringJob> _logger;

    public CommandCenterInsightsRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        ICommandCenterInsightRepository snapshotRepo,
        IEntitlementService entitlements,
        IMediator mediator,
        ILogger<CommandCenterInsightsRecurringJob> logger)
    {
        _visibilityRepo = visibilityRepo;
        _snapshotRepo = snapshotRepo;
        _entitlements = entitlements;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var organizationIds = await _visibilityRepo.GetAllOrganizationIdsAsync();

        _logger.LogInformation("CommandCenterInsightsRecurringJob: checking {Count} organizations", organizationIds.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await Parallel.ForEachAsync(organizationIds, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency }, async (organizationId, ct) =>
        {
            try
            {
                var scanIntervalDays = (int)(await _entitlements.GetPlanLimitValueAsync(organizationId, "recurring_scan_interval_days", ct) ?? DefaultScanIntervalDays);
                var latestScanDate = await _snapshotRepo.GetLatestScanDateAsync(organizationId);
                var isDue = latestScanDate == null || today.DayNumber - latestScanDate.Value.DayNumber >= scanIntervalDays;

                if (!isDue)
                {
                    return;
                }

                var result = await _mediator.Send(new RunCommandCenterInsightsCommand { OrganizationId = organizationId }, ct);
                _logger.LogInformation(
                    "CommandCenterInsightsRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommandCenterInsightsRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        });
    }
}
