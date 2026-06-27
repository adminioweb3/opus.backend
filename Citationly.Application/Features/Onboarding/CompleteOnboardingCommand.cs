using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Dapper;
using Hangfire;

namespace Citationly.Application.Features.Onboarding;

public class CompleteOnboardingCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public string WebsiteUrl { get; set; } = string.Empty;
    public string BusinessName { get; set; } = string.Empty;

    public int VisibilityScore { get; set; }
    public int BrandAuthority { get; set; }
    public int ContentStrength { get; set; }
    public int CitationScore { get; set; }
}

public class CompleteOnboardingCommandHandler : IRequestHandler<CompleteOnboardingCommand, bool>
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IScrapingJobRepository _scrapingJobRepository;

    public CompleteOnboardingCommandHandler(
        IDbConnectionFactory dbConnectionFactory,
        IBackgroundJobClient backgroundJobClient,
        IScrapingJobRepository scrapingJobRepository)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _backgroundJobClient = backgroundJobClient;
        _scrapingJobRepository = scrapingJobRepository;
    }

    public async Task<bool> Handle(CompleteOnboardingCommand request, CancellationToken cancellationToken)
    {
        using var connection = _dbConnectionFactory.CreateConnection();
        
        // 1. Update Organization Name
        if (!string.IsNullOrWhiteSpace(request.BusinessName))
        {
            await connection.ExecuteAsync(
                "UPDATE Organizations SET Name = @BusinessName WHERE Id = @OrganizationId",
                new { request.BusinessName, request.OrganizationId });
        }

        // 2. Insert or get Website
        Guid websiteId = Guid.Empty;
        if (!string.IsNullOrWhiteSpace(request.WebsiteUrl))
        {
            websiteId = await connection.QueryFirstOrDefaultAsync<Guid>(
                "SELECT Id FROM Websites WHERE OrganizationId = @OrganizationId AND DomainUrl = @WebsiteUrl",
                new { request.OrganizationId, request.WebsiteUrl });

            if (websiteId == Guid.Empty)
            {
                websiteId = await connection.QuerySingleAsync<Guid>(
                    "INSERT INTO Websites (OrganizationId, DomainUrl) VALUES (@OrganizationId, @WebsiteUrl) RETURNING Id",
                    new { request.OrganizationId, request.WebsiteUrl });
            }

            // 3. Enqueue Website Scraper (Website crawl)
            var job = new ScrapingJob
            {
                OrganizationId = request.OrganizationId,
                WebsiteId = websiteId,
                Url = request.WebsiteUrl,
                ScrapeType = "Website",
                MaxPages = 50, // Reasonable default for onboarding
                Status = "Pending"
            };

            var jobId = await _scrapingJobRepository.CreateJobAsync(job);
            _backgroundJobClient.Enqueue<IScrapingJobService>(x => x.ProcessJobAsync(jobId));
        }

        // 4. Enqueue AI Visibility Engine Analysis
        _backgroundJobClient.Enqueue<IAiVisibilityEngineService>(x => x.RunAnalysisAsync(request.OrganizationId));

        return true;
    }
}
