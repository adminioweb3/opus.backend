using MediatR;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Visibility;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzePlatformVisibilityCommand : IRequest<PlatformVisibilityAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class PlatformVisibilityAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PlatformsAnalyzed { get; set; }
    public Citationly.Domain.Entities.VisibilitySummary? Summary { get; set; }
    public List<PlatformVisibility>? PlatformScores { get; set; }
}

public class PlatformVisibilityResponse
{
    public PlatformVisibilitySummaryResponse? summary { get; set; }
    public List<PlatformVisibilityItemResponse>? platformScores { get; set; }
}

public class PlatformVisibilitySummaryResponse
{
    public double overallVisibilityScore { get; set; }
    public string? bestPlatform { get; set; }
    public string? weakestPlatform { get; set; }
    public double averageMentionRate { get; set; }
    public double averagePromptCoverage { get; set; }
}

public class PlatformVisibilityItemResponse
{
    public string? platform { get; set; }
    public double visibilityScore { get; set; }
    public string? averageRank { get; set; }
    public double mentionRate { get; set; }
    public double promptCoverage { get; set; }
    public double confidence { get; set; }
    public List<string>? strengths { get; set; }
    public List<string>? weaknesses { get; set; }
}

public class AnalyzePlatformVisibilityCommandHandler : IRequestHandler<AnalyzePlatformVisibilityCommand, PlatformVisibilityAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IVisibilityScoringService _scoringService;
    private readonly IVisibilityRankingService _rankingService;
    private readonly IVisibilityCacheService _cacheService;
    private readonly IVisibilityBatchProcessor _batchProcessor;

    public AnalyzePlatformVisibilityCommandHandler(
        IWebsiteRepository websiteRepository,
        IVisibilityScoringService scoringService,
        IVisibilityRankingService rankingService,
        IVisibilityCacheService cacheService,
        IVisibilityBatchProcessor batchProcessor)
    {
        _websiteRepository = websiteRepository;
        _scoringService = scoringService;
        _rankingService = rankingService;
        _cacheService = cacheService;
        _batchProcessor = batchProcessor;
    }

    public async Task<PlatformVisibilityAnalysisResult> Handle(AnalyzePlatformVisibilityCommand request, CancellationToken cancellationToken)
    {
        // 0. Check Cache
        var existingSummary = await _cacheService.GetCachedSummaryAsync(request.OrganizationId);
        var existingPlatforms = await _cacheService.GetCachedPlatformVisibilitiesAsync(request.OrganizationId);
        
        // Return cached results if they exist (frontend will fetch them)
        if (existingSummary != null && existingPlatforms != null && existingPlatforms.Any())
        {
            return new PlatformVisibilityAnalysisResult
            {
                Success = true,
                PlatformsAnalyzed = existingPlatforms.Count,
                Summary = existingSummary,
                PlatformScores = existingPlatforms
            };
        }

        // 1. Get required data
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
        if (profile == null)
            return new PlatformVisibilityAnalysisResult { Success = false, Error = "Website profile not found." };

        var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
        var promptList = existingPrompts?.ToList() ?? new List<AiSearchPrompt>();
        
        if (!promptList.Any())
            return new PlatformVisibilityAnalysisResult { Success = false, Error = "No AI search prompts found." };

        try
        {
            // 2. Stage 1: Deterministic Engine (Math only, no AI)
            var visibilities = _scoringService.CalculatePlatformScores(request.OrganizationId, promptList);
            var summary = _rankingService.CalculateOverallSummary(request.OrganizationId, visibilities);

            // 3. Save baseline numeric scores to DB immediately
            await _websiteRepository.InsertPlatformVisibilityAsync(summary, visibilities);

            // 4. Stage 2: Queue for AI Background Enrichment
            foreach (var platform in visibilities)
            {
                await _batchProcessor.QueueInsightTaskAsync(new PlatformInsightTask
                {
                    PlatformVisibility = platform,
                    Profile = profile,
                    Prompts = promptList
                });
            }

            // 5. Return fast response
            return new PlatformVisibilityAnalysisResult
            {
                Success = true,
                PlatformsAnalyzed = visibilities.Count,
                Summary = summary,
                PlatformScores = visibilities
            };
        }
        catch (Exception ex)
        {
            return new PlatformVisibilityAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
