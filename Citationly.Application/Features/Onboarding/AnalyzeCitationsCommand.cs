using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Citations;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeCitationsCommand : IRequest<CitationAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class CitationAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int SourcesAnalyzed { get; set; }
    public CitationSummary? Summary { get; set; }
    public List<CitationSource>? Sources { get; set; }
}

public class AnalyzeCitationsCommandHandler : IRequestHandler<AnalyzeCitationsCommand, CitationAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICitationDiscoveryService _discoveryService;
    private readonly ICitationBatchProcessor _batchProcessor;
    private readonly ICitationAnalyticsService _analyticsService;

    public AnalyzeCitationsCommandHandler(
        IWebsiteRepository websiteRepository,
        ICitationDiscoveryService discoveryService,
        ICitationBatchProcessor batchProcessor,
        ICitationAnalyticsService analyticsService)
    {
        _websiteRepository = websiteRepository;
        _discoveryService = discoveryService;
        _batchProcessor = batchProcessor;
        _analyticsService = analyticsService;
    }

    public async Task<CitationAnalysisResult> Handle(AnalyzeCitationsCommand request, CancellationToken cancellationToken)
    {
        // 0. Check if already analyzed
        var existingSources = (await _websiteRepository.GetCitationSourcesAsync(request.OrganizationId)).ToList();
        if (existingSources.Any())
        {
            var summary = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId);
            return new CitationAnalysisResult
            {
                Success = true,
                SourcesAnalyzed = existingSources.Count,
                Summary = summary,
                Sources = existingSources.Take(40).ToList() // Return top 40 for UI
            };
        }

        // 1. Get required data
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
            return new CitationAnalysisResult { Success = false, Error = "Website profile not found." };

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        var platformVisibilities = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);

        var promptAnalysisJson = JsonSerializer.Serialize(existingPrompts.Select(p => new
        {
            Query = p.QueryString,
            VisibilityScore = p.VisibilityScore,
            BrandStrength = p.BrandStrength
        }));

        var platformScoresJson = JsonSerializer.Serialize(platformVisibilities.Select(p => new
        {
            Platform = p.Platform,
            VisibilityScore = p.VisibilityScore
        }));

        try
        {
            // 2. Discover Initial Sources (Fast AI Call)
            var discoveredSources = await _discoveryService.DiscoverCitationsAsync(
                request.OrganizationId,
                profile.WebsiteUrl,
                profile.RawProfileJson,
                promptAnalysisJson,
                platformScoresJson
            );

            if (!discoveredSources.Any())
                return new CitationAnalysisResult { Success = false, Error = "No citation sources could be identified." };

            // 3. Create dummy summary (will be updated by background batch processing)
            var initialSummary = new CitationSummary
            {
                Id = Guid.NewGuid(),
                OrganizationId = request.OrganizationId,
                TotalSources = discoveredSources.Count,
                AverageAuthorityScore = 0,
                AverageInfluenceScore = 0,
                HighestOpportunitySource = "",
                MostInfluentialSource = "",
                CreatedAt = DateTime.UtcNow
            };

            // 4. Save initial data to DB
            await _websiteRepository.InsertCitationsAsync(initialSummary, discoveredSources);

            // 5. Queue Background Enrichment
            await _batchProcessor.QueueCitationEnrichmentAsync(request.OrganizationId);

            return new CitationAnalysisResult
            {
                Success = true,
                SourcesAnalyzed = discoveredSources.Count,
                Summary = initialSummary,
                Sources = discoveredSources.Take(40).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CitationAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
