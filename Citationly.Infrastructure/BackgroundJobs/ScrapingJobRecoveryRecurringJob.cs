using Citationly.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Citationly.Infrastructure.BackgroundJobs;

/// <summary>
/// Returns crawler jobs stranded by a process interruption to the existing explicit-retry flow.
/// It never retries a crawl automatically, because re-crawling can create avoidable provider load.
/// </summary>
public sealed class ScrapingJobRecoveryRecurringJob
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    private readonly IScrapingJobRepository _repository;
    private readonly ILogger<ScrapingJobRecoveryRecurringJob> _logger;

    public ScrapingJobRecoveryRecurringJob(
        IScrapingJobRepository repository,
        ILogger<ScrapingJobRecoveryRecurringJob> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var recovered = await _repository.MarkStaleProcessingJobsFailedAsync(StaleAfter);
        if (recovered > 0)
        {
            _logger.LogWarning("ScrapingJobRecoveryRecurringJob: marked {Count} stale processing job(s) as failed", recovered);
        }
    }
}
