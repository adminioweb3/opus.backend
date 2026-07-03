using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Citationly.Application.Interfaces.Onboarding;

namespace Citationly.Application.Features.Onboarding;

public class GenerateRecommendationsCommand : IRequest<GenerateRecommendationsResult>
{
    public Guid OrganizationId { get; set; }
}

public class GenerateRecommendationsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public GeoRecommendationSummaryResponse? summary { get; set; }
    public List<GeoRecommendationResponse>? recommendations { get; set; }
}

public class GeoRecommendationResponseWrapper
{
    public GeoRecommendationSummaryResponse? summary { get; set; }
    public List<GeoRecommendationResponse>? recommendations { get; set; }
}

public class GeoRecommendationSummaryResponse
{
    public string? overallPriority { get; set; }
    public string? estimatedOverallImpact { get; set; }
    public string? estimatedImplementationTime { get; set; }
    public int totalRecommendations { get; set; }
    public int criticalRecommendations { get; set; }
    public int highPriorityRecommendations { get; set; }
}

public class GeoRecommendationResponse
{
    public string? recommendationId { get; set; }
    public string? category { get; set; }
    public string? title { get; set; }
    public string? description { get; set; }
    public string? priority { get; set; }
    public string? estimatedImpact { get; set; }
    public string? estimatedDifficulty { get; set; }
    public string? implementationTime { get; set; }
    public string? expectedOutcome { get; set; }
    public string? successMetric { get; set; }
    public JsonElement actionItems { get; set; }
}

public class GenerateRecommendationsCommandHandler : IRequestHandler<GenerateRecommendationsCommand, GenerateRecommendationsResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IGeoRecommendationCacheService _cacheService;
    private readonly IGapDetectionService _gapDetectionService;
    private readonly IRecommendationDiscoveryService _discoveryService;
    private readonly IRoadmapService _roadmapService;
    private readonly IRecommendationEnrichmentQueue _enrichmentQueue;

    public GenerateRecommendationsCommandHandler(
        IWebsiteRepository websiteRepository,
        IGeoRecommendationCacheService cacheService,
        IGapDetectionService gapDetectionService,
        IRecommendationDiscoveryService discoveryService,
        IRoadmapService roadmapService,
        IRecommendationEnrichmentQueue enrichmentQueue)
    {
        _websiteRepository = websiteRepository;
        _cacheService = cacheService;
        _gapDetectionService = gapDetectionService;
        _discoveryService = discoveryService;
        _roadmapService = roadmapService;
        _enrichmentQueue = enrichmentQueue;
    }

    public async Task<GenerateRecommendationsResult> Handle(GenerateRecommendationsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var existingSummary = await _websiteRepository.GetGeoRecommendationSummaryAsync(request.OrganizationId);
            
            // 1. Cache Check
            if (await _cacheService.IsCacheValidAsync(request.OrganizationId, existingSummary))
            {
                var existingRecs = await _websiteRepository.GetGeoRecommendationsAsync(request.OrganizationId);
                var summaryResponse = existingSummary != null ? new GeoRecommendationSummaryResponse
                {
                    overallPriority = existingSummary.OverallPriority,
                    estimatedOverallImpact = existingSummary.EstimatedOverallImpact,
                    estimatedImplementationTime = existingSummary.EstimatedImplementationTime,
                    totalRecommendations = existingSummary.TotalRecommendations,
                    criticalRecommendations = existingSummary.CriticalRecommendations,
                    highPriorityRecommendations = existingSummary.HighPriorityRecommendations
                } : null;

                var recommendationsResponse = existingRecs.Select(r => new GeoRecommendationResponse
                {
                    recommendationId = r.Id.ToString(),
                    category = r.Category,
                    title = r.Title,
                    description = r.Description,
                    priority = r.Priority,
                    estimatedImpact = r.EstimatedImpact,
                    estimatedDifficulty = r.EstimatedDifficulty,
                    implementationTime = r.ImplementationTime,
                    expectedOutcome = r.ExpectedOutcome,
                    successMetric = r.SuccessMetric,
                    actionItems = !string.IsNullOrEmpty(r.ActionItemsJson) ? JsonDocument.Parse(r.ActionItemsJson).RootElement : default
                }).ToList();

                return new GenerateRecommendationsResult { Success = true, summary = summaryResponse, recommendations = recommendationsResponse };
            }

            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
            if (profile == null) return new GenerateRecommendationsResult { Success = false, Error = "No profile" };

            // Fetch dependencies for gap detection
            var visibilitySum = await _websiteRepository.GetVisibilitySummaryAsync(request.OrganizationId);
            var platVis = await _websiteRepository.GetPlatformVisibilitiesAsync(request.OrganizationId);
            var citationsSum = await _websiteRepository.GetCitationSummaryAsync(request.OrganizationId);
            var personasSum = await _websiteRepository.GetPersonaAnalysisSummaryAsync(request.OrganizationId);
            var regionsSum = await _websiteRepository.GetRegionAnalysisSummaryAsync(request.OrganizationId);

            // 2. Gap Detection (Deterministic)
            var gapAnalysis = await _gapDetectionService.DetectGapsAsync(
                profile, visibilitySum, platVis, citationsSum, personasSum, regionsSum);

            // 3. Recommendation Discovery (AI - Lightweight)
            var discoveredRecs = await _discoveryService.DiscoverRecommendationsAsync(
                gapAnalysis, profile.WebsiteUrl, profile.RawProfileJson);

            if (discoveredRecs == null || !discoveredRecs.Any())
            {
                return new GenerateRecommendationsResult { Success = false, Error = "Failed to discover recommendations." };
            }

            // 4. Roadmap Generation (Deterministic)
            var roadmap = _roadmapService.GenerateRoadmap(request.OrganizationId, discoveredRecs, gapAnalysis);

            // 5. Save to DB
            await _websiteRepository.InsertGeoRecommendationsAsync(roadmap.Summary, roadmap.Recommendations);

            // 6. Queue Background Enrichment
            _enrichmentQueue.EnqueueEnrichment(request.OrganizationId);

            var newSummaryResponse = new GeoRecommendationSummaryResponse
            {
                overallPriority = roadmap.Summary.OverallPriority,
                estimatedOverallImpact = roadmap.Summary.EstimatedOverallImpact,
                estimatedImplementationTime = roadmap.Summary.EstimatedImplementationTime,
                totalRecommendations = roadmap.Summary.TotalRecommendations,
                criticalRecommendations = roadmap.Summary.CriticalRecommendations,
                highPriorityRecommendations = roadmap.Summary.HighPriorityRecommendations
            };

            var newRecsResponse = roadmap.Recommendations.Select(r => new GeoRecommendationResponse
            {
                recommendationId = r.Id.ToString(),
                category = r.Category,
                title = r.Title,
                description = r.Description,
                priority = r.Priority,
                estimatedImpact = r.EstimatedImpact,
                estimatedDifficulty = r.EstimatedDifficulty,
                implementationTime = r.ImplementationTime,
                expectedOutcome = r.ExpectedOutcome,
                successMetric = r.SuccessMetric,
                actionItems = !string.IsNullOrEmpty(r.ActionItemsJson) ? JsonDocument.Parse(r.ActionItemsJson).RootElement : default
            }).ToList();

            return new GenerateRecommendationsResult { Success = true, summary = newSummaryResponse, recommendations = newRecsResponse };
        }
        catch (Exception ex)
        {
            return new GenerateRecommendationsResult { Success = false, Error = ex.Message };
        }
    }
}
