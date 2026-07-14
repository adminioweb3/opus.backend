using System.Text.Json;
using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Citations;

public class RunCitationScanCommand : IRequest<RunCitationScanResult>
{
    public Guid OrganizationId { get; set; }
}

public record RunCitationScanResult(bool Success, string Message);

public class RunCitationScanCommandHandler : IRequestHandler<RunCitationScanCommand, RunCitationScanResult>
{
    private const int SourcesPerScan = 30;

    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICitationDiscoveryService _discoveryService;
    private readonly ICitationEnrichmentService _enrichmentService;
    private readonly ICitationScanSnapshotRepository _snapshotRepo;
    private readonly IVisibilitySnapshotRepository _visibilitySnapshotRepo;

    public RunCitationScanCommandHandler(
        IWebsiteRepository websiteRepository,
        ICitationDiscoveryService discoveryService,
        ICitationEnrichmentService enrichmentService,
        ICitationScanSnapshotRepository snapshotRepo,
        IVisibilitySnapshotRepository visibilitySnapshotRepo)
    {
        _websiteRepository = websiteRepository;
        _discoveryService = discoveryService;
        _enrichmentService = enrichmentService;
        _snapshotRepo = snapshotRepo;
        _visibilitySnapshotRepo = visibilitySnapshotRepo;
    }

    public async Task<RunCitationScanResult> Handle(RunCitationScanCommand request, CancellationToken cancellationToken)
    {
        await _snapshotRepo.EnsureTableCreatedAsync();

        var orgId = request.OrganizationId;

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(orgId);
        if (profile == null)
        {
            return new RunCitationScanResult(false, "No analyzed data found yet for this organization. Complete onboarding analysis first, then run a citation scan.");
        }

        // Reuse already-discovered sources (from onboarding or a prior scan) so we don't
        // re-run the expensive discovery call every week — only re-score (enrich) them.
        var existingSources = (await _websiteRepository.GetCitationSourcesAsync(orgId))
            .OrderBy(s => s.Rank)
            .Take(SourcesPerScan)
            .ToList();

        List<CitationSource> candidates;
        if (existingSources.Count > 0)
        {
            candidates = existingSources;
        }
        else
        {
            var prompts = (await _websiteRepository.GetAiSearchPromptsAsync(orgId)).ToList();
            var platformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(orgId);

            var promptAnalysisJson = JsonSerializer.Serialize(prompts.Select(p => new { Query = p.QueryString, p.VisibilityScore, p.BrandStrength }));
            var platformScoresJson = JsonSerializer.Serialize(platformVisibilities.Select(p => new { p.Platform, p.VisibilityScore }));

            var discovered = await _discoveryService.DiscoverCitationsAsync(orgId, profile.WebsiteUrl, profile.RawProfileJson, promptAnalysisJson, platformScoresJson);
            candidates = discovered.OrderBy(s => s.Rank).Take(SourcesPerScan).ToList();
        }

        if (candidates.Count == 0)
        {
            return new RunCitationScanResult(false, "No citation sources could be identified for this organization.");
        }

        // Real AI scoring for this week's snapshot — always re-scored fresh, regardless of
        // whether these sources were already enriched by the onboarding pipeline, so the
        // weekly trend reflects a genuinely re-judged (not stale, cached) assessment.
        var enriched = await _enrichmentService.EnrichCitationsAsync(orgId, profile.RawProfileJson, candidates);

        var avgAuthority = (int)Math.Round(enriched.Average(s => s.AuthorityScore));
        var avgInfluence = (int)Math.Round(enriched.Average(s => s.InfluenceScore));
        var citationSignal = (int)Math.Round(enriched.Average(s => s.CitationFrequency));
        var compositeQuality = (int)Math.Round((avgAuthority + avgInfluence) / 2.0);

        // "Models referencing" reuses the sibling Visibility scan's latest real per-platform
        // scores rather than re-deriving platform coverage from scratch.
        var visLatestDate = await _visibilitySnapshotRepo.GetLatestScanDateAsync(orgId);
        var platformSnaps = visLatestDate.HasValue
            ? await _visibilitySnapshotRepo.GetPlatformSnapshotsByScanDateAsync(orgId, visLatestDate.Value)
            : new List<VisibilityPlatformSnapshot>();
        var modelsTracked = platformSnaps.Count > 0 ? platformSnaps.Count : 9;
        var modelsReferencing = platformSnaps.Count(p => p.Score >= 20);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _snapshotRepo.DeleteByScanDateAsync(orgId, today);

        await _snapshotRepo.InsertSummaryAsync(new CitationScanSummary
        {
            OrganizationId = orgId,
            ScanDate = today,
            CompositeQualityScore = compositeQuality,
            AverageAuthorityScore = avgAuthority,
            AverageInfluenceScore = avgInfluence,
            CitationSignal = citationSignal,
            ModelsReferencingCount = modelsReferencing,
            ModelsTrackedCount = modelsTracked
        });

        foreach (var source in enriched)
        {
            await _snapshotRepo.InsertSourceSnapshotAsync(new CitationSourceSnapshot
            {
                OrganizationId = orgId,
                ScanDate = today,
                Source = source.Source,
                Category = source.Category,
                AuthorityScore = source.AuthorityScore,
                InfluenceScore = source.InfluenceScore,
                CitationFrequency = source.CitationFrequency,
                CompetitorCoverage = source.CompetitorCoverage,
                OpportunityScore = source.OpportunityScore,
                MentionProbability = source.MentionProbability,
                Reason = source.Reason
            });
        }

        return new RunCitationScanResult(true, "Citation scan complete.");
    }
}
