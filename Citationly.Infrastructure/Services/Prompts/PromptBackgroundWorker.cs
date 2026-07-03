using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Citationly.Application.Interfaces.Prompts;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Prompts;

public class PromptBackgroundWorker : BackgroundService
{
    private readonly IPromptBatchProcessor _batchProcessor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PromptBackgroundWorker> _logger;

    public PromptBackgroundWorker(
        IPromptBatchProcessor batchProcessor, 
        IServiceProvider serviceProvider,
        ILogger<PromptBackgroundWorker> logger)
    {
        _batchProcessor = batchProcessor;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PromptBackgroundWorker is starting.");

        try
        {
            await foreach (var prompts in _batchProcessor.DequeuePromptsAsync(stoppingToken))
            {
                // We use Task.Run so we can process multiple batches in parallel if we were to pull multiple items
                // but since Dequeue gives us one list (which contains all 50-100 prompts for one site),
                // we should chunk it into batches of 10 and process them in parallel.
                _ = ProcessPromptsInBatchesAsync(prompts, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("PromptBackgroundWorker is stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred executing PromptBackgroundWorker.");
        }
    }

    private async Task ProcessPromptsInBatchesAsync(List<AiSearchPrompt> prompts, CancellationToken stoppingToken)
    {
        try
        {
            var batchSize = 10;
            var maxDegreeOfParallelism = 5;

            var chunks = prompts
                .Select((x, i) => new { Index = i, Value = x })
                .GroupBy(x => x.Index / batchSize)
                .Select(x => x.Select(v => v.Value).ToList())
                .ToList();

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = stoppingToken
            };

            await Parallel.ForEachAsync(chunks, parallelOptions, async (chunk, token) =>
            {
                await ProcessSingleBatchAsync(chunk);
            });
            
            _logger.LogInformation($"Successfully completed enriching {prompts.Count} prompts.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing prompt chunks in background worker.");
        }
    }

    private async Task ProcessSingleBatchAsync(List<AiSearchPrompt> batch)
    {
        // Must create a new scope since we are in a background singleton service
        using var scope = _serviceProvider.CreateScope();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<IPromptEnrichmentService>();
        
        int retryCount = 3;
        for (int i = 0; i < retryCount; i++)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                await enrichmentService.EnrichPromptsBatchAsync(batch);
                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation($"Enriched batch of {batch.Count} prompts in {duration.TotalMilliseconds}ms.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Attempt {i + 1} failed for enriching batch.");
                if (i == retryCount - 1)
                {
                    _logger.LogError(ex, "Failed to enrich prompt batch after maximum retries.");
                }
                else
                {
                    await Task.Delay(2000 * (i + 1)); // Exponential backoff
                }
            }
        }
    }
}
