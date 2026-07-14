using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.BrandPulse;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's brand pulse once 7+ days
/// have passed since its last BrandPulseScanSummary, so Brand Pulse stays fresh automatically.
/// </summary>
public class BrandPulseScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IBrandPulseSnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<BrandPulseScanRecurringJob> _logger;

    public BrandPulseScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        IBrandPulseSnapshotRepository snapshotRepo,
        IMediator mediator,
        ILogger<BrandPulseScanRecurringJob> logger)
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

        _logger.LogInformation("BrandPulseScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunBrandPulseScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "BrandPulseScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BrandPulseScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
