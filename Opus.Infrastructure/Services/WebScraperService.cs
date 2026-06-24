using HtmlAgilityPack;
using Opus.Application.Interfaces;
using Opus.Domain.Entities;

namespace Opus.Infrastructure.Services;

public class WebScraperService : IWebScraperService
{
    public async Task<List<CrawledPage>> ScrapeWebsiteAsync(Guid websiteId, string domainUrl)
    {
        var pages = new List<CrawledPage>();
        var web = new HtmlWeb();

        try
        {
            // Simple logic: Just scrape the homepage for the demo
            var doc = await web.LoadFromWebAsync(domainUrl);
            
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            var title = titleNode?.InnerText ?? "No Title";

            // Extract text without HTML tags for the LLM
            var text = string.Join(" ", doc.DocumentNode.SelectNodes("//text()[normalize-space(.) != '']")
                ?.Select(n => n.InnerText.Trim()) ?? Enumerable.Empty<string>());

            pages.Add(new CrawledPage
            {
                WebsiteId = websiteId,
                Url = domainUrl,
                Title = title,
                Content = text,
                LastCrawledAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            // Log error
            Console.WriteLine($"Error crawling {domainUrl}: {ex.Message}");
        }

        return pages;
    }
}
