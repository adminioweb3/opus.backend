using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Prompts;

public interface IPromptDiscoveryService
{
    Task<List<AiSearchPrompt>> DiscoverPromptsAsync(Guid organizationId, string websiteProfile);
}
