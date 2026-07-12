using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Opportunities;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's opportunities once 7+ days
/// have passed since its last OpportunitySnapshot, so Opportunity Finder stays fresh
/// automatically — this is the same 7-day cadence enforced client-facing by the manual
/// "Run deep scan" button's cooldown.
/// </summary>
public class OpportunityScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IOpportunitySnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<OpportunityScanRecurringJob> _logger;

    public OpportunityScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        IOpportunitySnapshotRepository snapshotRepo,
        IMediator mediator,
        ILogger<OpportunityScanRecurringJob> logger)
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

        _logger.LogInformation("OpportunityScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunOpportunityScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "OpportunityScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpportunityScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
