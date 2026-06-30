using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IWebScraperService
{
    Task<List<CrawledPage>> ScrapeWebsiteAsync(Guid websiteId, string domainUrl);
}
