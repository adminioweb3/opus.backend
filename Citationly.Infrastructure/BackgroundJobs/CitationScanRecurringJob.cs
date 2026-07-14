using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Citations;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's citation sources once
/// 7+ days have passed since its last CitationScanSummary, so Citation Intelligence stays
/// fresh automatically.
/// </summary>
public class CitationScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICitationScanSnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<CitationScanRecurringJob> _logger;

    public CitationScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        ICitationScanSnapshotRepository snapshotRepo,
        IMediator mediator,
        ILogger<CitationScanRecurringJob> logger)
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

        _logger.LogInformation("CitationScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunCitationScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "CitationScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CitationScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
