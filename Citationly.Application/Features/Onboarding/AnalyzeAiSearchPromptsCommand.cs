using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Onboarding;

public class AnalyzeAiSearchPromptsCommand : IRequest<AiSearchPromptsAnalysisResult>
{
    public Guid OrganizationId { get; set; }
}

public class AiSearchPromptsAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalPrompts { get; set; }
    public List<AiSearchPrompt>? Prompts { get; set; }
}

public class AnalyzeAiSearchPromptsCommandHandler : IRequestHandler<AnalyzeAiSearchPromptsCommand, AiSearchPromptsAnalysisResult>
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IPromptCacheService _promptCacheService;
    private readonly IPromptDiscoveryService _promptDiscoveryService;
    private readonly IPromptBatchProcessor _promptBatchProcessor;

    public AnalyzeAiSearchPromptsCommandHandler(
        IWebsiteRepository websiteRepository,
        IPromptCacheService promptCacheService,
        IPromptDiscoveryService promptDiscoveryService,
        IPromptBatchProcessor promptBatchProcessor)
    {
        _websiteRepository = websiteRepository;
        _promptCacheService = promptCacheService;
        _promptDiscoveryService = promptDiscoveryService;
        _promptBatchProcessor = promptBatchProcessor;
    }

    public async Task<AiSearchPromptsAnalysisResult> Handle(AnalyzeAiSearchPromptsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Get the latest Website Profile
            var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(request.OrganizationId);
            if (profile == null)
            {
                return new AiSearchPromptsAnalysisResult { Success = false, Error = "Website profile not found for this organization." };
            }

            // 2. Check if prompts already exist in the DB for this organization
            var existingPrompts = await _websiteRepository.GetAiSearchPromptsAsync(request.OrganizationId);
            var promptList = existingPrompts?.ToList();

            if (promptList != null && promptList.Any())
            {
                // Prompts already exist, skip discovery and return top 40
                return new AiSearchPromptsAnalysisResult
                {
                    Success = true,
                    TotalPrompts = promptList.Count,
                    Prompts = promptList.Take(40).ToList()
                };
            }

            // 3. Stage 1: Prompt Discovery (Fast sync call)
            var discoveredPrompts = await _promptDiscoveryService.DiscoverPromptsAsync(request.OrganizationId, profile.RawProfileJson);

            if (discoveredPrompts == null || !discoveredPrompts.Any())
            {
                return new AiSearchPromptsAnalysisResult { Success = false, Error = "Failed to discover AI prompts." };
            }

            // 4. Save Initial Discovery Prompts (so the user sees them immediately)
            await _websiteRepository.InsertAiSearchPromptsAsync(discoveredPrompts);

            // 5. Queue Stage 2: Prompt Enrichment (Background processing)
            await _promptBatchProcessor.QueuePromptEnrichmentAsync(discoveredPrompts);

            return new AiSearchPromptsAnalysisResult
            {
                Success = true,
                TotalPrompts = discoveredPrompts.Count,
                Prompts = discoveredPrompts.Take(40).ToList()
            };
        }
        catch (Exception ex)
        {
            return new AiSearchPromptsAnalysisResult { Success = false, Error = ex.Message };
        }
    }
}
