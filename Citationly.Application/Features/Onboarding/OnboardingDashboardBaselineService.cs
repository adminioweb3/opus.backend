using Citationly.Application.Features.Competitors;
using Citationly.Application.Features.Metrics;
using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Application.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Citationly.Application.Features.Onboarding;

public sealed record OnboardingDashboardBaselineStatus(
    bool Ready,
    bool PromptEvidenceReady,
    bool GeoDashboardReady,
    bool CompetitorWatchReady);

public interface IOnboardingDashboardBaselineService
{
    Task RunAsync(Guid organizationId);
    Task<OnboardingDashboardBaselineStatus> GetStatusAsync(Guid organizationId);
}

/// <summary>
/// Builds onboarding's first measured dashboard baseline in dependency order. Prompt evidence
/// must exist before GEO and competitor snapshots are calculated; running these as independent
/// background jobs allowed the snapshot jobs to win the race and persist empty/default results.
/// </summary>
public sealed class OnboardingDashboardBaselineService : IOnboardingDashboardBaselineService
{
    private readonly IPromptIntelligenceFirstRunService _firstRunService;
    private readonly IPromptIntelligenceRepository _promptRepository;
    private readonly IAiVisibilityRepository _visibilityRepository;
    private readonly ICompetitorSnapshotRepository _competitorSnapshotRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<OnboardingDashboardBaselineService> _logger;

    public OnboardingDashboardBaselineService(
        IPromptIntelligenceFirstRunService firstRunService,
        IPromptIntelligenceRepository promptRepository,
        IAiVisibilityRepository visibilityRepository,
        ICompetitorSnapshotRepository competitorSnapshotRepository,
        IMediator mediator,
        ILogger<OnboardingDashboardBaselineService> logger)
    {
        _firstRunService = firstRunService;
        _promptRepository = promptRepository;
        _visibilityRepository = visibilityRepository;
        _competitorSnapshotRepository = competitorSnapshotRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task RunAsync(Guid organizationId)
    {
        await _firstRunService.RunFirstBatchAsync(organizationId);

        var geoResult = await _mediator.Send(new RunScanCommand { OrganizationId = organizationId });
        if (!geoResult.Success)
            _logger.LogWarning("Initial GEO baseline was not created for org {OrganizationId}: {Message}", organizationId, geoResult.Message);

        var competitorResult = await _mediator.Send(new RunCompetitorScanCommand { OrganizationId = organizationId });
        if (!competitorResult.Success)
            _logger.LogWarning("Initial competitor baseline was not created for org {OrganizationId}: {Message}", organizationId, competitorResult.Message);
    }

    public async Task<OnboardingDashboardBaselineStatus> GetStatusAsync(Guid organizationId)
    {
        var since = DateTime.UtcNow.AddDays(-90);
        var promptEvidenceReady = (await _promptRepository.GetVisibilitySummaryDataAsync(organizationId, since)).Any();
        var geoDashboardReady = (await _visibilityRepository.GetHistoricalScansByOrgAsync(organizationId))
            .Any(scan => new[]
            {
                scan.VisibilityScore, scan.CitationScore, scan.SentimentScore, scan.CompetitorScore,
                scan.HallucinationRisk, scan.SeoHealth, scan.AeoReadiness, scan.GeoReadiness
            }.Any(score => score > 0));

        var latestCompetitorDate = await _competitorSnapshotRepository.GetLatestScanDateAsync(organizationId);
        var competitorWatchReady = latestCompetitorDate.HasValue &&
            (await _competitorSnapshotRepository.GetSnapshotsByScanDateAsync(organizationId, latestCompetitorDate.Value))
                .Any(snapshot => snapshot.MeasurementSource == "openai-observed" && snapshot.IsYou);

        return new OnboardingDashboardBaselineStatus(
            promptEvidenceReady && geoDashboardReady && competitorWatchReady,
            promptEvidenceReady,
            geoDashboardReady,
            competitorWatchReady);
    }
}
