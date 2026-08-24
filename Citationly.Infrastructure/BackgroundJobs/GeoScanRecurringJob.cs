using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization once its plan's configured
/// interval (PlanLimits "recurring_scan_interval_days" - Trial=7, Pro=1, Enterprise=1, default
/// 7 if unset) has passed since its last HistoricalScan, so higher tiers get fresher data
/// without every org running at the same unconditional cadence. Processes organizations with
/// bounded concurrency rather than one giant sequential loop, per the audit's cost-scale finding.
/// </summary>
public class GeoScanRecurringJob
{
    private const int DefaultScanIntervalDays = 7;
    private const int MaxConcurrency = 5;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly IEntitlementService _entitlements;
    private readonly IMediator _mediator;
    private readonly ILogger<GeoScanRecurringJob> _logger;

    public GeoScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        IEntitlementService entitlements,
        IMediator mediator,
        ILogger<GeoScanRecurringJob> logger)
    {
        _visibilityRepo = visibilityRepo;
        _entitlements = entitlements;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var organizationIds = await _visibilityRepo.GetAllOrganizationIdsAsync();

        _logger.LogInformation("GeoScanRecurringJob: checking {Count} organizations", organizationIds.Count);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await Parallel.ForEachAsync(organizationIds, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency }, async (organizationId, ct) =>
        {
            try
            {
                var scanIntervalDays = (int)(await _entitlements.GetPlanLimitValueAsync(organizationId, "recurring_scan_interval_days", ct) ?? DefaultScanIntervalDays);
                var scans = await _visibilityRepo.GetHistoricalScansByOrgAsync(organizationId);
                var latestScan = scans.OrderByDescending(s => s.ScanDate).FirstOrDefault();
                var isDue = latestScan == null || today.DayNumber - latestScan.ScanDate.DayNumber >= scanIntervalDays;

                if (!isDue)
                {
                    return;
                }

                var result = await _mediator.Send(new RunScanCommand { OrganizationId = organizationId }, ct);
                _logger.LogInformation(
                    "GeoScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeoScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        });
    }
}
