using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Visibility;

public interface IPlatformInsightService
{
    Task GenerateInsightAsync(PlatformVisibility platform, WebsiteProfile profile, List<AiSearchPrompt> prompts);
}
