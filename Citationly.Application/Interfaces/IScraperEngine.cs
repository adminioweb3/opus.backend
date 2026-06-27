using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IScraperEngine
{
    Task<ScrapedPage> ScrapeSinglePageAsync(string url, Guid jobId);
    Task<List<ScrapedPage>> ScrapeWebsiteAsync(string startUrl, Guid jobId, int maxPages, Action<int>? progressCallback = null);
}
