using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Visibility;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's AI-platform visibility once
/// 7+ days have passed since its last VisibilityScanSummary, so Visibility Radar stays fresh
/// automatically.
/// </summary>
public class VisibilityScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IVisibilitySnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<VisibilityScanRecurringJob> _logger;

    public VisibilityScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        IVisibilitySnapshotRepository snapshotRepo,
        IMediator mediator,
        ILogger<VisibilityScanRecurringJob> logger)
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

        _logger.LogInformation("VisibilityScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunVisibilityScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "VisibilityScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VisibilityScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
