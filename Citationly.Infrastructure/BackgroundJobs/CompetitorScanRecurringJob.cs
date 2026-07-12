using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.Competitors;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire-invoked, runs daily. Only re-scans an organization's competitors once 7+ days
/// have passed since its last CompetitorSnapshot, so each org stays on its own 7-day cadence.
/// </summary>
public class CompetitorScanRecurringJob
{
    private const int ScanIntervalDays = 7;

    private readonly IAiVisibilityRepository _visibilityRepo;
    private readonly ICompetitorSnapshotRepository _snapshotRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<CompetitorScanRecurringJob> _logger;

    public CompetitorScanRecurringJob(
        IAiVisibilityRepository visibilityRepo,
        ICompetitorSnapshotRepository snapshotRepo,
        IMediator mediator,
        ILogger<CompetitorScanRecurringJob> logger)
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

        _logger.LogInformation("CompetitorScanRecurringJob: checking {Count} organizations", organizationIds.Count);

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

                var result = await _mediator.Send(new RunCompetitorScanCommand { OrganizationId = organizationId });
                _logger.LogInformation(
                    "CompetitorScanRecurringJob: org {OrganizationId} success={Success} message={Message}",
                    organizationId, result.Success, result.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompetitorScanRecurringJob: scan failed for org {OrganizationId}", organizationId);
            }
        }
    }
}
