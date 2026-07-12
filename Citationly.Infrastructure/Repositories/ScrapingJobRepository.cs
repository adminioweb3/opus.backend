using System.Data;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;

namespace Citationly.Infrastructure.Repositories;

public class ScrapingJobRepository : IScrapingJobRepository
{
    private readonly IDbConnectionFactory _dbConnectionFactory;

    public ScrapingJobRepository(IDbConnectionFactory dbConnectionFactory)
    {
        _dbConnectionFactory = dbConnectionFactory;
    }

    public async Task<Guid> CreateJobAsync(ScrapingJob job)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO ScrapingJobs (OrganizationId, WebsiteId, KnowledgeBaseId, Url, Status, ScrapeType, TotalPages, ProcessedPages, MaxPages, SuccessfulPages, FailedPages, TotalWords, TotalImages, TotalLinks, StartedAt, CompletedAt, CreatedAt)
            VALUES (@OrganizationId, @WebsiteId, @KnowledgeBaseId, @Url, @Status, @ScrapeType, @TotalPages, @ProcessedPages, @MaxPages, @SuccessfulPages, @FailedPages, @TotalWords, @TotalImages, @TotalLinks, @StartedAt, @CompletedAt, NOW())
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, job);
    }

    public async Task UpdateJobAsync(ScrapingJob job)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            UPDATE ScrapingJobs 
            SET Status = @Status,
                TotalPages = @TotalPages,
                ProcessedPages = @ProcessedPages,
                SuccessfulPages = @SuccessfulPages,
                FailedPages = @FailedPages,
                TotalWords = @TotalWords,
                TotalImages = @TotalImages,
                TotalLinks = @TotalLinks,
                StartedAt = @StartedAt,
                CompletedAt = @CompletedAt
            WHERE Id = @Id;";
        await connection.ExecuteAsync(sql, job);
    }

    public async Task<ScrapingJob?> GetJobAsync(Guid jobId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ScrapingJob>(
            "SELECT * FROM ScrapingJobs WHERE Id = @Id;", new { Id = jobId });
    }

    public async Task<ScrapingJob?> GetActiveJobForUrlAsync(Guid organizationId, string url)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ScrapingJob>(
            @"SELECT * FROM ScrapingJobs
              WHERE OrganizationId = @OrganizationId
                AND LOWER(TRIM(Url)) = LOWER(TRIM(@Url))
                AND Status IN ('Pending', 'Processing')
              ORDER BY CreatedAt DESC
              LIMIT 1;",
            new { OrganizationId = organizationId, Url = url });
    }

    public async Task DeleteJobAsync(Guid jobId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        // ScrapedPages cascade on delete
        await connection.ExecuteAsync("DELETE FROM ScrapingJobs WHERE Id = @Id;", new { Id = jobId });
    }

    public async Task<List<ScrapingJob>> GetAllJobsByOrgAsync(Guid organizationId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<ScrapingJob>(
            "SELECT * FROM ScrapingJobs WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC;",
            new { OrganizationId = organizationId });
        return results.ToList();
    }

    public async Task<List<ScrapingJob>> GetJobsByOrgAndKbAsync(Guid organizationId, Guid knowledgeBaseId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<ScrapingJob>(
            "SELECT * FROM ScrapingJobs WHERE OrganizationId = @OrganizationId AND KnowledgeBaseId = @KnowledgeBaseId ORDER BY CreatedAt DESC;",
            new { OrganizationId = organizationId, KnowledgeBaseId = knowledgeBaseId });
        return results.ToList();
    }

    public async Task<Guid> InsertScrapedPageAsync(ScrapedPage page)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO ScrapedPages (JobId, Url, Title, Description, Content, HtmlContent, MarkdownContent, WordCount, ImageCount, LinkCount, Images, InternalLinks, ExternalLinks, Headings, ScrapedAt)
            VALUES (@JobId, @Url, @Title, @Description, @Content, @HtmlContent, @MarkdownContent, @WordCount, @ImageCount, @LinkCount,
                    @Images::jsonb, @InternalLinks::jsonb, @ExternalLinks::jsonb, @Headings::jsonb, NOW())
            ON CONFLICT (JobId, Url) DO UPDATE SET
                Title = EXCLUDED.Title,
                Description = EXCLUDED.Description,
                Content = EXCLUDED.Content,
                HtmlContent = EXCLUDED.HtmlContent,
                MarkdownContent = EXCLUDED.MarkdownContent,
                WordCount = EXCLUDED.WordCount,
                ImageCount = EXCLUDED.ImageCount,
                LinkCount = EXCLUDED.LinkCount,
                Images = EXCLUDED.Images,
                InternalLinks = EXCLUDED.InternalLinks,
                ExternalLinks = EXCLUDED.ExternalLinks,
                Headings = EXCLUDED.Headings,
                ScrapedAt = NOW()
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, page);
    }

    public async Task<List<ScrapedPage>> GetPagesByJobIdAsync(Guid jobId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<ScrapedPage>(
            "SELECT * FROM ScrapedPages WHERE JobId = @JobId ORDER BY ScrapedAt;",
            new { JobId = jobId });
        return results.ToList();
    }

    public async Task<List<ScrapedPage>> GetPagesByKnowledgeBaseAsync(Guid organizationId, Guid knowledgeBaseId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var results = await connection.QueryAsync<ScrapedPage>(
            @"SELECT sp.* FROM ScrapedPages sp
              JOIN ScrapingJobs sj ON sp.JobId = sj.Id
              WHERE sj.OrganizationId = @OrganizationId AND sj.KnowledgeBaseId = @KnowledgeBaseId
              ORDER BY sp.ScrapedAt DESC;",
            new { OrganizationId = organizationId, KnowledgeBaseId = knowledgeBaseId });
        return results.ToList();
    }

    public async Task<ScrapedPage?> GetPageAsync(Guid pageId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ScrapedPage>(
            "SELECT * FROM ScrapedPages WHERE Id = @Id;", new { Id = pageId });
    }

    public async Task DeletePageAsync(Guid pageId)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        await connection.ExecuteAsync("DELETE FROM ScrapedPages WHERE Id = @Id;", new { Id = pageId });
    }

    public async Task<Guid> InsertExtractedImageAsync(ExtractedImage image)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO ExtractedImages (PageId, Url, AltText)
            VALUES (@PageId, @Url, @AltText)
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, image);
    }

    public async Task<Guid> InsertExtractedLinkAsync(ExtractedLink link)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO ExtractedLinks (PageId, Url, LinkType)
            VALUES (@PageId, @Url, @LinkType)
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, link);
    }

    public async Task<Guid> InsertWebsiteMetadataAsync(WebsiteMetadata metadata)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        var sql = @"
            INSERT INTO WebsiteMetadata (WebsiteId, JobId, Title, Description, OpenGraph, TwitterCard, SchemaData, JsonLd, CanonicalUrl, Robots, Language, CreatedAt)
            VALUES (@WebsiteId, @JobId, @Title, @Description, @OpenGraph::jsonb, @TwitterCard::jsonb, @SchemaData::jsonb, @JsonLd::jsonb, @CanonicalUrl, @Robots, @Language, NOW())
            RETURNING Id;";
        return await connection.QuerySingleAsync<Guid>(sql, metadata);
    }
}
