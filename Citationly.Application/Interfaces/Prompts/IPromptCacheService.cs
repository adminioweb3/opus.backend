using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Prompts;

public interface IPromptCacheService
{
    Task<List<AiSearchPrompt>> GetCachedPromptsAsync(Guid organizationId, string currentProfileHash);
    Task InvalidateCacheAsync(Guid organizationId);
}
