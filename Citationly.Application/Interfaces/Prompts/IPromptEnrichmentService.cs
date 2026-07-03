using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Prompts;

public interface IPromptEnrichmentService
{
    Task EnrichPromptsBatchAsync(List<AiSearchPrompt> batch);
}
