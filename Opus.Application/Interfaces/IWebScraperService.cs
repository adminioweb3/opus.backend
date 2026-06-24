using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface IWebScraperService
{
    Task<List<CrawledPage>> ScrapeWebsiteAsync(Guid websiteId, string domainUrl);
}
