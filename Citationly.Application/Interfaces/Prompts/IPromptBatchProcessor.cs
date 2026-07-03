using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Prompts;

public interface IPromptBatchProcessor
{
    ValueTask QueuePromptEnrichmentAsync(List<AiSearchPrompt> prompts);
    IAsyncEnumerable<List<AiSearchPrompt>> DequeuePromptsAsync(CancellationToken cancellationToken);
}
