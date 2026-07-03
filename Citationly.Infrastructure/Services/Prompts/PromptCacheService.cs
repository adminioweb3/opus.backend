using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptCacheService : IPromptCacheService
{
    private readonly IWebsiteRepository _websiteRepository;

    public PromptCacheService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<List<AiSearchPrompt>> GetCachedPromptsAsync(Guid organizationId, string currentProfileHash)
    {
        // Simple cache check: if prompts already exist for this organization, return them.
        var count = await _websiteRepository.GetAiSearchPromptCountAsync(organizationId);
        
        if (count > 0)
        {
            var prompts = await _websiteRepository.GetAiSearchPromptsAsync(organizationId);
            return prompts.ToList();
        }

        return new List<AiSearchPrompt>();
    }

    public Task InvalidateCacheAsync(Guid organizationId)
    {
        // Future implementation: remove prompts or mark them as stale
        return Task.CompletedTask;
    }
}
