using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Visibility;

public class PlatformInsightTask
{
    public PlatformVisibility PlatformVisibility { get; set; } = null!;
    public WebsiteProfile Profile { get; set; } = null!;
    public List<AiSearchPrompt> Prompts { get; set; } = new();
}

public interface IVisibilityBatchProcessor
{
    ValueTask QueueInsightTaskAsync(PlatformInsightTask task);
    IAsyncEnumerable<PlatformInsightTask> DequeueTasksAsync(CancellationToken cancellationToken);
}
