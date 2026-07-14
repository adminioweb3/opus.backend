using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.CommandCenter;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only regenerates an organization's Command Center business
/// insights once 7+ days have passed since its last CommandCenterInsightSnapshot.
/// </summary>
public class CommandCenterInsightsRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICommandCenterInsightRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<CommandCenterInsightsRecurringJob> _logger;

    public CommandCenterInsightsRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        ICommandCenterInsightRepository snapshotRepo,
        IMediator mediator,
        ILogger<CommandCenterInsightsRecurringJob> logger)
    {
        _visibilityRepo = visibilityRepo;
        _snapshotRepo = snapshotRepo;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var organizationIds = await _visibilityRepo.GetAllOrganizationIdsAsync();

        _logger.LogInformation("CommandCenterInsightsRecurringJob: checking {Count} organizations", organizationIds.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var organizationId in organizationIds)
        {
            try
            {
                var latestScanDate = await _snapshotRepo.GetLatestScanDateAsync(organizationId);
                var isDue = latestScanDate == null || today.DayNumber - latestScanDate.Value.DayNumber >= ScanIntervalDays;

                if (!isDue)
                {
                    continue;
                }

                var result = await _mediator.Send(new RunCommandCenterInsightsCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "CommandCenterInsightsRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CommandCenterInsightsRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
