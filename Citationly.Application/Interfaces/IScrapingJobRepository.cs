using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IScrapingJobRepository
{
    Task<Guid> CreateJobAsync(ScrapingJob job);
    Task UpdateJobAsync(ScrapingJob job);
    Task<ScrapingJob?> GetJobAsync(Guid jobId);
    Task<ScrapingJob?> GetActiveJobForUrlAsync(Guid organizationId, string url);
    Task DeleteJobAsync(Guid jobId);
    Task<List<ScrapingJob>> GetAllJobsByOrgAsync(Guid organizationId, int limit = 100);
    Task<List<ScrapingJob>> GetJobsByOrgAndKbAsync(Guid organizationId, Guid knowledgeBaseId, int limit = 100);
    Task<Guid> InsertScrapedPageAsync(ScrapedPage page);
    Task<List<ScrapedPage>> GetPagesByJobIdAsync(Guid jobId, int limit = 250);
    Task<List<ScrapedPage>> GetPagesByKnowledgeBaseAsync(Guid organizationId, Guid knowledgeBaseId, int limit = 250);
    Task<ScrapedPage?> GetPageAsync(Guid pageId);
    Task DeletePageAsync(Guid pageId);
    Task<Guid> InsertExtractedImageAsync(ExtractedImage image);
    Task<Guid> InsertExtractedLinkAsync(ExtractedLink link);
    Task<Guid> InsertWebsiteMetadataAsync(WebsiteMetadata metadata);
}
