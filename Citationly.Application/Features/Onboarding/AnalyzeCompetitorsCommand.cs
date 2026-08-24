using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeCompetitorsCommand : IRequest<CompetitorAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class CompetitorAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalCompetitors { get; set; }
    public object? Competitors { get; set; }
    public bool EnrichmentQueued { get; set; }
}

/// <summary>
/// Real competitor discovery over the shared Company Knowledge Graph: ensures the org's own
/// Company node is up to date, ranks it against real candidates already in the graph (never
/// invents a company), and materializes the result into the existing per-org Competitors table
/// so every existing reader keeps working unchanged.
/// </summary>
public class AnalyzeCompetitorsCommandHandler : IRequestHandler<AnalyzeCompetitorsCommand, CompetitorAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompetitorDiscoveryService _discoveryService;
    private readonly ICompetitorCacheService _cacheService;
    private readonly ICompanyGraphService _companyGraphService;
    private readonly ICompanyCompetitorRepository _companyCompetitorRepository;
    private readonly ICompetitorGraphSyncService _syncService;

    public AnalyzeCompetitorsCommandHandler(
        IWebsiteRepository websiteRepository,
        ICompetitorDiscoveryService discoveryService,
        ICompetitorCacheService cacheService,
        ICompanyGraphService companyGraphService,
        ICompanyCompetitorRepository companyCompetitorRepository,
        ICompetitorGraphSyncService syncService)
    {
        _websiteRepository = websiteRepository;
        _discoveryService = discoveryService;
        _cacheService = cacheService;
        _companyGraphService = companyGraphService;
        _companyCompetitorRepository = companyCompetitorRepository;
        _syncService = syncService;
    }

    public async Task<CompetitorAnalysisResult> Handle(AnalyzeCompetitorsCommand request, CancellationToken cancellationToken)
    {
        var (isValid, cachedCompetitors) = await _cacheService.TryGetCachedAsync(request.OrganizationId, cancellationToken);
        if (isValid && cachedCompetitors != null)
        {
            var cachedList = cachedCompetitors.ToList();
            return new CompetitorAnalysisResult
            {
                Success = true,
                TotalCompetitors = cachedList.Count,
                Competitors = cachedList,
                EnrichmentQueued = false
            };
        }

        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
        {
            return new CompetitorAnalysisResult { Success = false, Error = "No website profile found. Run analysis step first." };
        }

        var company = await _companyGraphService.EnsureCompanyAsync(
            request.OrganizationId, profile.WebsiteUrl, profile.BusinessName, profile.RawProfileJson, cancellationToken);

        var edges = await _discoveryService.DiscoverCompetitorsAsync(
            request.OrganizationId, company.Id, profile.BusinessName, profile.RawProfileJson, cancellationToken);

        await _companyCompetitorRepository.ReplaceCompetitorsForCompanyAsync(company.Id, edges);

        var rows = await _syncService.SyncOrgCompetitorsAsync(request.OrganizationId, company.Id);

        return new CompetitorAnalysisResult
        {
            Success = true,
            TotalCompetitors = rows.Count,
            Competitors = rows,
            EnrichmentQueued = false
        };
    }
}
