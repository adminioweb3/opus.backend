using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Hangfire;
using Microsoft.AspNetCore.Mvc;

namespace Citationly.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ScraperController : ControllerBase
{
    private readonly IScrapingJobRepository _repository;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IMarkdownGeneratorService _markdownGenerator;

    public ScraperController(
        IScrapingJobRepository repository,
        IBackgroundJobClient backgroundJobClient,
        IMarkdownGeneratorService markdownGenerator)
    {
        _repository = repository;
        _backgroundJobClient = backgroundJobClient;
        _markdownGenerator = markdownGenerator;
    }

    // POST /api/scraper/start
    [HttpPost("start")]
    public async Task<IActionResult> StartScraping([FromBody] StartScrapeRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            return BadRequest(new { message = "OrganizationId is required." });

        var job = new ScrapingJob
        {
            OrganizationId = request.OrganizationId,
            Url = request.Url,
            ScrapeType = request.ScrapeType,
            MaxPages = request.MaxPages,
            Status = "Pending"
        };

        var jobId = await _repository.CreateJobAsync(job);

        _backgroundJobClient.Enqueue<IScrapingJobService>(x => x.ProcessJobAsync(jobId));

        return Ok(new { JobId = jobId, Status = "Pending" });
    }

    // GET /api/scraper/jobs?organizationId=xxx
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            return BadRequest(new { message = "OrganizationId is required." });

        var jobs = await _repository.GetAllJobsByOrgAsync(organizationId);
        
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
        if (job == null) return NotFound();

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

        var pageResults = pages.Select(p => new
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
        }).ToList();

        return Ok(new { Job = job, Pages = pageResults });
    }

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
    public Guid OrganizationId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ScrapeType { get; set; } = "Single";
    public int MaxPages { get; set; } = 100;
}
