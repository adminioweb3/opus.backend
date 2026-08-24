using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Citations;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's citation sources once its
/// plan's configured interval (PlanLimits "recurring_scan_interval_days", default 7 if unset)
/// has passed since its last CitationScanSummary. Processes organizations with bounded
/// concurrency rather than one giant sequential loop, per the audit's cost-scale finding.
/// </summary>
public class CitationScanRecurringJob
{
    private const int DefaultScanIntervalDays = 7;
    private const int MaxConcurrency = 5;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICitationScanSnapshotRepository _snapshotRepo;
    private readonly IEntitlementService _entitlements;
    private readonly IMediator _mediator;
    private readonly ILogger<CitationScanRecurringJob> _logger;

    public CitationScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        ICitationScanSnapshotRepository snapshotRepo,
        IEntitlementService entitlements,
        IMediator mediator,
        ILogger<CitationScanRecurringJob> logger)
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

        _logger.LogInformation("CitationScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunCitationScanCommand { OrganizationId = organizationId }, ct);
                _logger.LogInformation(
                    "CitationScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CitationScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        });
    }
}
