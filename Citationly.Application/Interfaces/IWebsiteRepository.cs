using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IWebsiteRepository
{
    Task<Guid> GetOrInsertWebsiteAsync(Guid organizationId, string domainUrl);
    Task<IEnumerable<Website>> GetAllWebsitesAsync();
    Task<IEnumerable<Website>> GetWebsitesByOrgAsync(Guid organizationId);
    Task<Website> ConnectWebsiteAsync(Guid organizationId, string domainUrl, string platformName);
    Task<Guid> InsertCrawledPageAsync(CrawledPage page);
    Task<Guid> InsertRecommendationAsync(Recommendation recommendation);
}
