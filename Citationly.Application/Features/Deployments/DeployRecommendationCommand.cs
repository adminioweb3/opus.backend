using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Deployments;

public class DeployRecommendationCommand : IRequest<string>
{
    public Guid OrganizationId { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid IntegrationId { get; set; }
    public string Status { get; set; } = "draft";
}

public class DeployRecommendationCommandHandler : IRequestHandler<DeployRecommendationCommand, string>
{
    private readonly IOpenAiService _openRouterService;
    private readonly ICmsIntegrationService _cmsIntegrationService;
    // We would normally need IRecommendationRepository and IIntegrationRepository here,
    // but for the sake of MVP we will simulate fetching them or inject them if they exist.

    public DeployRecommendationCommandHandler(
        IOpenAiService openRouterService,
        ICmsIntegrationService cmsIntegrationService)
    {
        _openRouterService = openRouterService;
        _cmsIntegrationService = cmsIntegrationService;
    }

    public async Task<string> Handle(DeployRecommendationCommand request, CancellationToken cancellationToken)
    {
        // 1. Fetch Recommendation (Simulated for MVP)
        var recommendationTitle = "Optimize your CRM landing page for 'best CRM for startups'";
        var recommendationDescription = "Your CRM landing page is missing key AEO terms related to startups. Add a section addressing specific startup pain points.";

        // 2. Fetch Integration (Simulated for MVP)
        var dummyIntegration = new Domain.Entities.Integration
        {
            Id = request.IntegrationId,
            OrganizationId = request.OrganizationId,
            PlatformName = "WordPress",
            ApiUrl = "https://example.com",
            ApiKey = "dummy"
        };

        // 3. Generate Content via AI
        var prompt = $"Create a blog post addressing this SEO recommendation: Title: {recommendationTitle}, Description: {recommendationDescription}";
        var generatedContent = await _openRouterService.GenerateContentAsync(prompt);

        // 4. Deploy Content
        string deployedUrl;
        if (dummyIntegration.ApiUrl == "https://example.com")
        {
            // Simulate WordPress response for the UI Demo
            await Task.Delay(1500); // simulate network latency
            deployedUrl = $"https://your-wordpress-site.com/draft?id={Guid.NewGuid().ToString().Substring(0, 8)}";
        }
        else
        {
            deployedUrl = await _cmsIntegrationService.DeployContentAsync(
                dummyIntegration, 
                recommendationTitle, 
                generatedContent, 
                request.Status);
        }

        // 5. Update Recommendation Status
        // _recommendationRepository.UpdateStatusAsync(request.RecommendationId, "Deployed", deployedUrl);

        return deployedUrl;
    }
}
