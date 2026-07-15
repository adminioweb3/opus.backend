using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
[Authorize]
public class ScraperController : ControllerBase
{
    private readonly IScrapingJobRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IMarkdownGeneratorService _markdownGenerator;
    private readonly ILogger<ScraperController> _logger;

    public ScraperController(
        IScrapingJobRepository repository,
        IBackgroundJobClient backgroundJobClient,
        IMarkdownGeneratorService markdownGenerator,
        ILogger<ScraperController> logger)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
        _markdownGenerator = markdownGenerator;
        _logger = logger;
    }

    // POST /api/scraper/start
    [HttpPost("start")]
    public async Task<IActionResult> StartScraping([FromBody] StartScrapeRequest request)
    {
        if (string.IsNullOrEmpty(request.OrganizationId))
            return BadRequest(new { message = "OrganizationId is required." });

        if (!Guid.TryParse(request.OrganizationId, out var orgGuid) || orgGuid == Guid.Empty)
        {
            // If it's a mock string like "org_acme_001", just return a dummy JobId
            // so the frontend can still simulate the progress bar!
            return Ok(new { JobId = Guid.NewGuid(), Status = "Pending" });
        }

        try
        {
            // A scrape/crawl for this exact URL is already in flight — return that job
            // instead of starting a second, duplicate crawl of the same site.
            var existingJob = await _repository.GetActiveJobForUrlAsync(orgGuid, request.Url);
            if (existingJob != null)
            {
                return Ok(new { JobId = existingJob.Id, Status = existingJob.Status });
            }

            Guid? knowledgeBaseId = Guid.TryParse(request.KnowledgeBaseId, out var kbGuid) ? kbGuid : null;
            Guid? folderId = Guid.TryParse(request.FolderId, out var folderGuid) ? folderGuid : null;

            var job = new ScrapingJob
            {
                OrganizationId = orgGuid,
                KnowledgeBaseId = knowledgeBaseId,
                FolderId = folderId,
                Url = request.Url,
                ScrapeType = request.ScrapeType,
                MaxPages = request.MaxPages,
                Status = "Pending"
            };

            var jobId = await _repository.CreateJobAsync(job);
            _backgroundJobClient.Enqueue<IScrapingJobService>(x => x.ProcessJobAsync(jobId));
            return Ok(new { JobId = jobId, Status = "Pending" });
        }
        catch (Exception ex)
        {
            // Previously returned a fake, never-persisted JobId here so the frontend could still
            // "simulate" a Pending job — that just guaranteed a later 404 on /result/{jobId} once
            // the frontend polled it, and hid whatever the real failure was (FK violation, schema
            // issue, etc.) from ever being logged. Surface it for real instead.
            _logger.LogError(ex, "Failed to create scraping job for organization {OrganizationId}, url {Url}", request.OrganizationId, request.Url);
            return StatusCode(500, new { message = "Failed to start scraping job. Please try again." });
        }
    }

    // GET /api/scraper/jobs?organizationId=xxx&knowledgeBaseId=yyy
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] Guid organizationId, [FromQuery] Guid? knowledgeBaseId)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { message = "OrganizationId is required." });

        var jobs = knowledgeBaseId.HasValue && knowledgeBaseId.Value != Guid.Empty
            ? await _repository.GetJobsByOrgAndKbAsync(organizationId, knowledgeBaseId.Value)
            : await _repository.GetAllJobsByOrgAsync(organizationId);

        var result = new List<object>();
        foreach (var job in jobs)
        {
            // Calculate rough size estimate (100 bytes per word on average)
            var sizeKb = (job.ProcessedPages * 50); // rough estimate in KB

            result.Add(new
            {
                job.Id,
                job.Url,
                job.Status,
                job.ScrapeType,
                job.KnowledgeBaseId,
                job.FolderId,
                job.TotalPages,
                job.ProcessedPages,
                job.MaxPages,
                job.CreatedAt,
                job.StartedAt,
                job.CompletedAt,
                EstimatedSizeKb = sizeKb,
                DisplayName = GetDisplayName(job)
            });
        }

        return Ok(result);
    }

    // GET /api/scraper/status/{jobId}
    [HttpGet("status/{jobId}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _repository.GetJobAsync(jobId);
        if (job == null) 
        {
            // If the job wasn't found (e.g. dummy JobId for mock data),
            // simulate a completed status so the frontend onboarding can proceed.
            return Ok(new { Status = "Completed", ProcessedPages = 15, TotalPages = 15, MaxPages = 15, StartedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow });
        }

        return Ok(new
        {
            job.Status,
            job.ProcessedPages,
            job.TotalPages,
            job.MaxPages,
            job.StartedAt,
            job.CompletedAt
        });
    }

    // GET /api/scraper/result/{jobId}
    [HttpGet("result/{jobId}")]
    public async Task<IActionResult> GetResult(Guid jobId)
    {
        var job = await _repository.GetJobAsync(jobId);
        if (job == null) return NotFound();

        var pages = await _repository.GetPagesByJobIdAsync(jobId);
        var pageResults = pages.Select(MapPageResult).ToList();

        return Ok(new { Job = job, Pages = pageResults });
    }

    // GET /api/scraper/page/{pageId}
    [HttpGet("page/{pageId}")]
    public async Task<IActionResult> GetPage(Guid pageId)
    {
        var page = await _repository.GetPageAsync(pageId);
        if (page == null) return NotFound();

        return Ok(MapPageResult(page));
    }

    private static object MapPageResult(ScrapedPage p) => new
    {
        p.Id,
        p.JobId,
        p.Url,
        p.Title,
        p.Description,
        p.Content,
        p.MarkdownContent,
        p.WordCount,
        p.ScrapedAt,
        Headings = TryDeserialize<List<object>>(p.Headings) ?? new List<object>(),
        InternalLinks = TryDeserialize<List<string>>(p.InternalLinks) ?? new List<string>(),
        ExternalLinks = TryDeserialize<List<string>>(p.ExternalLinks) ?? new List<string>(),
        Images = TryDeserialize<List<object>>(p.Images) ?? new List<object>(),
        SizeBytes = (p.MarkdownContent?.Length ?? p.Content?.Length ?? 0),
        FileName = GetPageFileName(p),
        UrlPath = GetUrlPath(p.Url),
        SubFolder = GetSubFolder(p.Url)
    };

    // GET /api/scraper/download/{jobId}
    [HttpGet("download/{jobId}")]
    public async Task<IActionResult> DownloadMarkdown(Guid jobId)
    {
        var job = await _repository.GetJobAsync(jobId);
        if (job == null) return NotFound();

        var pages = await _repository.GetPagesByJobIdAsync(jobId);
        var markdown = _markdownGenerator.AggregateMarkdown(pages);

        var bytes = Encoding.UTF8.GetBytes(markdown);
        var fileName = $"{GetDisplayName(job).Replace(" ", "_")}.md";
        return File(bytes, "text/markdown", fileName);
    }

    // GET /api/scraper/download-page/{pageId}
    [HttpGet("download-page/{pageId}")]
    public async Task<IActionResult> DownloadPage(Guid pageId)
    {
        // We need to find the page — add a GetPageAsync method or search in pages
        // For now, get by finding job via jobId embedded in the page 
        // Quick approach: query directly via Dapper through the repository
        // We'll get the page from the result endpoint 
        // Since we don't have a direct GetPageAsync, we'll use a workaround:
        // Return 404 for now and handle via the result endpoint
        return NotFound(new { message = "Use /result/{jobId} to get page content and download from client." });
    }

    // DELETE /api/scraper/jobs/{jobId}
    [HttpDelete("jobs/{jobId}")]
    public async Task<IActionResult> DeleteJob(Guid jobId)
    {
        var job = await _repository.GetJobAsync(jobId);
        if (job == null) return NotFound();

        await _repository.DeleteJobAsync(jobId);
        return Ok(new { message = "Job deleted successfully." });
    }

    // DELETE /api/scraper/pages/{pageId}
    [HttpDelete("pages/{pageId}")]
    public async Task<IActionResult> DeletePage(Guid pageId)
    {
        await _repository.DeletePageAsync(pageId);
        return Ok(new { message = "Page deleted successfully." });
    }

    private static string GetDisplayName(ScrapingJob job)
    {
        try
        {
            var uri = new Uri(job.Url);
            var domain = uri.Host.Replace("www.", "");
            return job.ScrapeType == "Website" ? $"{domain} (Crawl)" : domain;
        }
        catch
        {
            return job.Url;
        }
    }

    private static string GetPageFileName(ScrapedPage page)
    {
        var name = page.Title ?? page.Url;
        // Make file-system safe
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Replace(' ', '_').Trim('_');
        if (name.Length > 80) name = name[..80];
        return name + ".md";
    }

    private static T? TryDeserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch { return null; }
    }

    private static string GetUrlPath(string url)
    {
        try { return new Uri(url).AbsolutePath; }
        catch { return "/"; }
    }

    /// <summary>
    /// Returns the first path segment of the URL for subfolder grouping.
    /// e.g. https://ioweb3.io/blog/post-1 → "blog"
    ///      https://ioweb3.io/expertise/react → "expertise"
    ///      https://ioweb3.io/ → "" (root)
    /// </summary>
    private static string GetSubFolder(string url)
    {
        try
        {
            var segments = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length >= 2 ? segments[0] : "";
        }
        catch { return ""; }
    }
}


public class StartScrapeRequest
{
    public string OrganizationId { get; set; } = string.Empty;
    public string? KnowledgeBaseId { get; set; }
    public string? FolderId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ScrapeType { get; set; } = "Single";
    public int MaxPages { get; set; } = 100;
}
