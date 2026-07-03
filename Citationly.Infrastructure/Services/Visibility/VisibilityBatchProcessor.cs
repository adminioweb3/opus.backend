using System.Threading.Channels;
using Citationly.Application.Interfaces.Visibility;

namespace Citationly.Infrastructure.Services.Visibility;

public class VisibilityBatchProcessor : IVisibilityBatchProcessor
{
    private readonly Channel<PlatformInsightTask> _channel;

    public VisibilityBatchProcessor()
    {
        _channel = Channel.CreateUnbounded<PlatformInsightTask>();
    }

    public async ValueTask QueueInsightTaskAsync(PlatformInsightTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        await _channel.Writer.WriteAsync(task);
    }

    public IAsyncEnumerable<PlatformInsightTask> DequeueTasksAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
