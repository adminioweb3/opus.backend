using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization once 7+ days have
/// passed since its last HistoricalScan, so orgs stay on their own 7-day cadence
/// regardless of when they onboarded (and a missed run self-corrects the next day).
/// </summary>
public class GeoScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<GeoScanRecurringJob> _logger;

    public GeoScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        IMediator mediator,
        ILogger<GeoScanRecurringJob> logger)
    {
        _visibilityRepo = visibilityRepo;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var organizationIds = await _visibilityRepo.GetAllOrganizationIdsAsync();

        _logger.LogInformation("GeoScanRecurringJob: checking {Count} organizations", organizationIds.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var organizationId in organizationIds)
        {
            try
            {
                var scans = await _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId);
                var latestScan = scans.OrderByDescending(s => s.ScanDate).FirstOrDefault();
                var isDue = latestScan == null || today.DayNumber - latestScan.ScanDate.DayNumber >= ScanIntervalDays;

                if (!isDue)
                {
                    continue;
                }

                var result = await _mediator.Send(new RunScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "GeoScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeoScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
