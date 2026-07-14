using Hangfire;

namespace Citationly.Application.Interfaces;

public interface IScrapingJobService
{
    // A failed crawl should be retried explicitly by the user (via the UI), not silently
    // re-run by Hangfire — an automatic retry would re-crawl the whole site and re-insert
    // every page a second time.
    [AutomaticRetry(Attempts = 0)]
    Task ProcessJobAsync(Guid jobId);
}
