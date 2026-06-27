using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Websites;

public class AnalyzeWebsiteCommand : IRequest<List<Recommendation>>
{
    public Guid OrganizationId { get; set; }
    public string DomainUrl { get; set; } = string.Empty;
}

public class AnalyzeWebsiteCommandHandler : IRequestHandler<AnalyzeWebsiteCommand, List<Recommendation>>
{
    private readonly IWebScraperService _scraperService;
    private readonly IAiAnalysisService _aiService;
    private readonly IWebsiteRepository _repository;
    private readonly IMetricsRepository _metricsRepository;

    public AnalyzeWebsiteCommandHandler(
        IWebScraperService scraperService, 
        IAiAnalysisService aiService, 
        IWebsiteRepository repository,
        IMetricsRepository metricsRepository)
    {
        _scraperService = scraperService;
        _aiService = aiService;
        _repository = repository;
        _metricsRepository = metricsRepository;
    }

    public async Task<List<Recommendation>> Handle(AnalyzeWebsiteCommand request, CancellationToken cancellationToken)
    {
        var allRecommendations = new List<Recommendation>();

        // 1. Check or Insert Website via Repository
        var websiteId = await _repository.GetOrInsertWebsiteAsync(request.OrganizationId, request.DomainUrl);

        // 2. Generate and save competitors via AI
        var competitors = await _aiService.GenerateCompetitorsAsync(request.DomainUrl, request.OrganizationId);
        
        var mockScan = new HistoricalScan
        {
            OrganizationId = request.OrganizationId,
            ScanDate = DateOnly.FromDateTime(DateTime.UtcNow),
            VisibilityScore = new Random().Next(40, 85),
            CitationScore = new Random().Next(30, 75),
            SentimentScore = new Random().Next(50, 95),
            CompetitorScore = new Random().Next(45, 80)
        };
        
        await _metricsRepository.InsertMockScanAsync(mockScan, competitors);

        // 3. Scrape the website
        var scrapedPages = await _scraperService.ScrapeWebsiteAsync(websiteId, request.DomainUrl);

        // 4. For each page, save to DB and run AI analysis
        foreach (var page in scrapedPages)
        {
            var pageId = await _repository.InsertCrawledPageAsync(page);
            page.Id = pageId;

            // Run AI
            var recommendations = await _aiService.AnalyzePageAsync(page);

            // Save recommendations
            foreach (var rec in recommendations)
            {
                rec.WebsiteId = websiteId;
                rec.CrawledPageId = pageId;

                var recId = await _repository.InsertRecommendationAsync(rec);
                rec.Id = recId;
                
                allRecommendations.Add(rec);
            }
        }

        return allRecommendations;
    }
}
