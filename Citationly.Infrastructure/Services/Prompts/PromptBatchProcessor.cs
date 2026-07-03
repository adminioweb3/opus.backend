using System.Threading.Channels;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptBatchProcessor : IPromptBatchProcessor
{
    private readonly Channel<List<AiSearchPrompt>> _channel;

    public PromptBatchProcessor()
    {
        // Unbounded channel for simplicity, can be bounded to prevent memory pressure
        _channel = Channel.CreateUnbounded<List<AiSearchPrompt>>();
    }

    public async ValueTask QueuePromptEnrichmentAsync(List<AiSearchPrompt> prompts)
    {
        ArgumentNullException.ThrowIfNull(prompts);

        await _channel.Writer.WriteAsync(prompts);
    }

    public IAsyncEnumerable<List<AiSearchPrompt>> DequeuePromptsAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
