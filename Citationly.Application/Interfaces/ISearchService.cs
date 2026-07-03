using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface ISearchService
{
    Task<IEnumerable<Competitor>> DiscoverCompetitorsAsync(Guid organizationId, string industry, string services);
    Task<IEnumerable<AiSearchPrompt>> GeneratePromptsAsync(Guid organizationId, string industry, string services);
    Task<IEnumerable<BrandMention>> ExecutePromptSearchAsync(AiSearchPrompt prompt, IEnumerable<Competitor> knownCompetitors, string userBrandName);
}
