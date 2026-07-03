using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class ScrapingJobService : IScrapingJobService
{
    private readonly IScrapingJobRepository _repository;
    private readonly IScraperEngine _scraperEngine;

    public ScrapingJobService(IScrapingJobRepository repository, IScraperEngine scraperEngine)
    {
        _repository = repository;
        _scraperEngine = scraperEngine;
    }

    public async Task ProcessJobAsync(Guid jobId)
    {
        var job = await _repository.GetJobAsync(jobId);
        if (job == null) return;

        job.Status = "Processing";
        job.StartedAt = DateTime.UtcNow;
        await _repository.UpdateJobAsync(job);

        var pagesToProcess = new List<ScrapedPage>();

        try
        {
            if (job.ScrapeType == "Single")
            {
                var page = await _scraperEngine.ScrapeSinglePageAsync(job.Url, jobId);
                pagesToProcess.Add(page);
                job.ProcessedPages = 1;
                job.TotalPages = 1;
                await _repository.UpdateJobAsync(job);
            }
            else
            {
                // For website crawl, use progress callback to update DB incrementally
                var pages = await _scraperEngine.ScrapeWebsiteAsync(
                    job.Url, jobId, job.MaxPages,
                    (count) =>
                    {
                        job.ProcessedPages = count;
                        // Fire-and-forget progress update (best effort)
                        _ = _repository.UpdateJobAsync(job);
                    });

                pagesToProcess.AddRange(pages);

                job.TotalPages = pages.Count;
                job.ProcessedPages = pages.Count;
                await _repository.UpdateJobAsync(job);
            }

            // Save normalized data
            foreach (var page in pagesToProcess)
            {
                try
                {
                    // Parse lists for counts
                    var images = JsonSerializer.Deserialize<List<ImageObj>>(page.Images) ?? new List<ImageObj>();
                    var internalLinks = JsonSerializer.Deserialize<List<string>>(page.InternalLinks) ?? new List<string>();
                    var externalLinks = JsonSerializer.Deserialize<List<string>>(page.ExternalLinks) ?? new List<string>();

                    page.ImageCount = images.Count;
                    page.LinkCount = internalLinks.Count + externalLinks.Count;

                    // Update job aggregates
                    job.TotalWords += page.WordCount;
                    job.TotalImages += page.ImageCount;
                    job.TotalLinks += page.LinkCount;

                    var pageId = await _repository.InsertScrapedPageAsync(page);

                    // Insert normalized images
                    foreach (var img in images)
                    {
                        await _repository.InsertExtractedImageAsync(new ExtractedImage
                        {
                            PageId = pageId,
                            Url = img.Src,
                            AltText = img.Alt
                        });
                    }

                    // Insert normalized links
                    foreach (var link in internalLinks)
                    {
                        await _repository.InsertExtractedLinkAsync(new ExtractedLink
                        {
                            PageId = pageId,
                            Url = link,
                            LinkType = "Internal"
                        });
                    }

                    foreach (var link in externalLinks)
                    {
                        await _repository.InsertExtractedLinkAsync(new ExtractedLink
                        {
                            PageId = pageId,
                            Url = link,
                            LinkType = "External"
                        });
                    }

                    job.SuccessfulPages++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing page {page.Url}: {ex.Message}");
                    job.FailedPages++;
                }
            }

            // Optional: Generate WebsiteMetadata based on home page
            var homePage = pagesToProcess.FirstOrDefault(p => p.Url == job.Url) ?? pagesToProcess.FirstOrDefault();
            if (homePage != null && job.WebsiteId.HasValue)
            {
                await _repository.InsertWebsiteMetadataAsync(new WebsiteMetadata
                {
                    WebsiteId = job.WebsiteId.Value,
                    JobId = job.Id,
                    Title = homePage.Title,
                    Description = homePage.Description
                });
            }

            job.Status = "Completed";
        }
        catch (Exception ex)
        {
            job.Status = "Failed";
            Console.WriteLine($"Scraping failed for job {jobId}: {ex.Message}");
        }

        job.CompletedAt = DateTime.UtcNow;
        await _repository.UpdateJobAsync(job);
    }
    
    private class ImageObj
    {
        public string Src { get; set; } = string.Empty;
        public string Alt { get; set; } = string.Empty;
    }
}
