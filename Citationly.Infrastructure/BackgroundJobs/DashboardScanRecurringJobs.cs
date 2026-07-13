using MediatR;
using Microsoft.Extensions.Logging;
using Citationly.Application.Features.BrandPulse;
using Citationly.Application.Features.Citations;
using Citationly.Application.Features.CommandCenter;
using Citationly.Application.Features.CompetitorWatch;
using Citationly.Application.Features.Visibility;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

// Hangfire recurring jobs (registered daily in Program.cs via RecurringJob.AddOrUpdate) that
// keep every organization's dashboard data fresh even if nobody visits the page. Each GET
// endpoint already bootstraps/refreshes on-demand when visited, so this is a belt-and-braces
// job — same 7-day staleness rule, just running independently of page views. Opportunity
// Finder is deliberately NOT included here: it's user-triggered only, gated by its own
// server-side 7-day cooldown on the manual "Run Deep Scan" action.
public class DashboardScanRecurringJobs
{
    private readonly IMediator _mediator;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepository;
    private readonly ICitationScanSnapshotRepository _citationScanSnapshotRepository;
    private readonly IBrandPulseSnapshotRepository _brandPulseSnapshotRepository;
    private readonly ICommandCenterRepository _commandCenterRepository;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepository;
    private readonly ILogger<DashboardScanRecurringJobs> _logger;

    public DashboardScanRecurringJobs(
        IMediator mediator,
        IWebsiteRepository websiteRepository,
        IVisibilitySnapshotRepository visibilitySnapshotRepository,
        ICitationScanSnapshotRepository citationScanSnapshotRepository,
        IBrandPulseSnapshotRepository brandPulseSnapshotRepository,
        ICommandCenterRepository commandCenterRepository,
        ICompetitorSnapshotRepository competitorSnapshotRepository,
        ILogger<DashboardScanRecurringJobs> logger)
    {
        _mediator = mediator;
        _websiteRepository = websiteRepository;
        _visibilitySnapshotRepository = visibilitySnapshotRepository;
        _citationScanSnapshotRepository = citationScanSnapshotRepository;
        _brandPulseSnapshotRepository = brandPulseSnapshotRepository;
        _commandCenterRepository = commandCenterRepository;
        _competitorSnapshotRepository = competitorSnapshotRepository;
        _logger = logger;
    }

    private async Task<List<Guid>> GetActiveOrganizationIdsAsync()
    {
        var websites = await _websiteRepository.GetAllWebsitesAsync();
        return websites.Select(w => w.OrganizationId).Distinct().ToList();
    }

    private static bool IsStale(DateOnly? latest)
    {
        if (latest == null) return true;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (today.DayNumber - latest.Value.DayNumber) >= 7;
    }

    public async Task RunVisibilityScansAsync()
    {
        foreach (var orgId in await GetActiveOrganizationIdsAsync())
        {
            try
            {
                var latest = await _visibilitySnapshotRepository.GetLatestScanDateAsync(orgId);
                if (IsStale(latest))
                {
                    await _mediator.Send(new RunVisibilityScanCommand { OrganizationId = orgId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Visibility Radar scan failed for organization {OrganizationId}", orgId);
            }
        }
    }

    public async Task RunCitationScansAsync()
    {
        foreach (var orgId in await GetActiveOrganizationIdsAsync())
        {
            try
            {
                var latest = await _citationScanSnapshotRepository.GetLatestScanDateAsync(orgId);
                if (IsStale(latest))
                {
                    await _mediator.Send(new RunCitationScanCommand { OrganizationId = orgId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Citation Intelligence scan failed for organization {OrganizationId}", orgId);
            }
        }
    }

    public async Task RunBrandPulseScansAsync()
    {
        foreach (var orgId in await GetActiveOrganizationIdsAsync())
        {
            try
            {
                var latest = await _brandPulseSnapshotRepository.GetLatestScanDateAsync(orgId);
                if (IsStale(latest))
                {
                    await _mediator.Send(new RunBrandPulseScanCommand { OrganizationId = orgId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Brand Pulse scan failed for organization {OrganizationId}", orgId);
            }
        }
    }

    public async Task RunCommandCenterInsightsAsync()
    {
        foreach (var orgId in await GetActiveOrganizationIdsAsync())
        {
            try
            {
                var latest = await _commandCenterRepository.GetLatestScanDateAsync(orgId);
                if (IsStale(latest))
                {
                    await _mediator.Send(new RunCommandCenterInsightsCommand { OrganizationId = orgId, RangeDays = 30 });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Command Center insights refresh failed for organization {OrganizationId}", orgId);
            }
        }
    }

    public async Task RunCompetitorScansAsync()
    {
        foreach (var orgId in await GetActiveOrganizationIdsAsync())
        {
            try
            {
                var latest = await _competitorSnapshotRepository.GetLatestScanDateAsync(orgId);
                if (IsStale(latest))
                {
                    await _mediator.Send(new RunCompetitorSnapshotCommand { OrganizationId = orgId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recurring Competitor Watch scan failed for organization {OrganizationId}", orgId);
            }
        }
    }
}
