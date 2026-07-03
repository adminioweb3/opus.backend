using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Citationly.Application.Interfaces.Visibility;

namespace Citationly.Infrastructure.Services.Visibility;

public class VisibilityBackgroundWorker : BackgroundService
{
    private readonly IVisibilityBatchProcessor _batchProcessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VisibilityBackgroundWorker> _logger;

    public VisibilityBackgroundWorker(
        IVisibilityBatchProcessor batchProcessor,
        IServiceProvider serviceProvider,
        ILogger<VisibilityBackgroundWorker> logger)
    {
        _batchProcessor = batchProcessor;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VisibilityBackgroundWorker is starting.");

        // Process in parallel with up to 9 concurrent tasks (1 per platform)
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 9,
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(_batchProcessor.DequeueTasksAsync(stoppingToken), options, async (task, token) =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var insightService = scope.ServiceProvider.GetRequiredService<IPlatformInsightService>();
                
                _logger.LogInformation("Starting AI Insight for platform {Platform}", task.PlatformVisibility.Platform);
                
                await insightService.GenerateInsightAsync(task.PlatformVisibility, task.Profile, task.Prompts);
                
                _logger.LogInformation("Finished AI Insight for platform {Platform}", task.PlatformVisibility.Platform);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AI Insight for platform {Platform}", task.PlatformVisibility?.Platform);
            }
        });
    }
}
