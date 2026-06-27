using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.BackgroundJobs;

public class RecurringScrapeService : BackgroundService
{
    private readonly ILogger<RecurringScrapeService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _scrapeInterval = TimeSpan.FromHours(24);

    public RecurringScrapeService(ILogger<RecurringScrapeService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Recurring Scrape Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Recurring Scrape Service is running at: {time}", DateTimeOffset.Now);

            try
            {
                await ProcessWebsitesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing websites in the background job.");
            }

            // Wait for the next interval before running again
            await Task.Delay(_scrapeInterval, stoppingToken);
        }

        _logger.LogInformation("Recurring Scrape Service is stopping.");
    }

    private async Task ProcessWebsitesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var websiteRepo = scope.ServiceProvider.GetRequiredService<IWebsiteRepository>();
        var scraperService = scope.ServiceProvider.GetRequiredService<IWebScraperService>();
        var aiService = scope.ServiceProvider.GetRequiredService<IAiAnalysisService>();

        var websites = await websiteRepo.GetAllWebsitesAsync();

        foreach (var website in websites)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation("Background crawling website: {DomainUrl}", website.DomainUrl);

            var scrapedPages = await scraperService.ScrapeWebsiteAsync(website.Id, website.DomainUrl);

            foreach (var page in scrapedPages)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var pageId = await websiteRepo.InsertCrawledPageAsync(page);
                page.Id = pageId;

                // Generate and save embedding for the crawled page
                var pageVector = await aiService.GenerateEmbeddingAsync(page.Content ?? page.Title ?? "");
                await scope.ServiceProvider.GetRequiredService<IEmbeddingRepository>().InsertEmbeddingAsync(new Citationly.Domain.Entities.Embedding
                {
                    OrganizationId = website.OrganizationId,
                    ReferenceId = pageId,
                    ReferenceType = "Page",
                    TextContent = page.Content ?? page.Title ?? "",
                    Vector = pageVector
                });

                var recommendations = await aiService.AnalyzePageAsync(page);

                foreach (var rec in recommendations)
                {
                    rec.WebsiteId = website.Id;
                    rec.CrawledPageId = pageId;
                    var recId = await websiteRepo.InsertRecommendationAsync(rec);
                    
                    // Generate and save embedding for recommendation
                    var recVector = await aiService.GenerateEmbeddingAsync(rec.Title + " " + rec.Description);
                    await scope.ServiceProvider.GetRequiredService<IEmbeddingRepository>().InsertEmbeddingAsync(new Citationly.Domain.Entities.Embedding
                    {
                        OrganizationId = website.OrganizationId,
                        ReferenceId = recId,
                        ReferenceType = "Recommendation",
                        TextContent = rec.Title + " " + rec.Description,
                        Vector = recVector
                    });
                }
            }

            // --- COMPETITOR INTELLIGENCE ENGINE ---
            var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
            var metricsService = scope.ServiceProvider.GetRequiredService<IMetricsCalculationService>();

            _logger.LogInformation("Running Competitor Intelligence for: {DomainUrl}", website.DomainUrl);
            
            var industry = "Web3 Development";
            var services = "Smart Contracts, dApps, Blockchain Consulting";
            
            var competitors = await searchService.DiscoverCompetitorsAsync(website.OrganizationId, industry, services);
            var prompts = await searchService.GeneratePromptsAsync(website.OrganizationId, industry, services);
            
            foreach(var prompt in prompts)
            {
                await searchService.ExecutePromptSearchAsync(prompt, competitors, website.DomainUrl);
            }

            await metricsService.CalculateAndStoreMetricsAsync(website.OrganizationId, DateTime.UtcNow.Date, website.DomainUrl);
        }
    }
}
