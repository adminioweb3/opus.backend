using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Deployments;

public class DeployRecommendationCommand : IRequest<DeployRecommendationResult>
{
    public Guid OrganizationId { get; set; }
    public Guid RecommendationId { get; set; }
    public Guid IntegrationId { get; set; }
    public string Status { get; set; } = "draft";
}

public record DeployRecommendationResult(bool Success, string Message, string? DeployedUrl);

public class DeployRecommendationCommandHandler : IRequestHandler<DeployRecommendationCommand, DeployRecommendationResult>
{
    private readonly IAiCompletionService _aiCompletionService;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IEnumerable<ICmsIntegrationService> _cmsServices;

    public DeployRecommendationCommandHandler(
        IAiCompletionService aiCompletionService,
        IWebsiteRepository websiteRepository,
        IIntegrationRepository integrationRepository,
        IEnumerable<ICmsIntegrationService> cmsServices)
    {
        _aiCompletionService = aiCompletionService;
        _websiteRepository = websiteRepository;
        _integrationRepository = integrationRepository;
        _cmsServices = cmsServices;
    }

    public async Task<DeployRecommendationResult> Handle(DeployRecommendationCommand request, CancellationToken cancellationToken)
    {
        var recommendation = await _websiteRepository.GetRecommendationByIdAsync(request.RecommendationId, request.OrganizationId);
        if (recommendation == null)
        {
            return new DeployRecommendationResult(false, "Recommendation not found.", null);
        }

        var integration = await _integrationRepository.GetIntegrationByIdAsync(request.IntegrationId, request.OrganizationId);
        if (integration == null || string.IsNullOrWhiteSpace(integration.ApiUrl) || string.IsNullOrWhiteSpace(integration.ApiKey))
        {
            return new DeployRecommendationResult(false, "No integration connected yet. Connect one first.", null);
        }

        var cmsService = _cmsServices.FirstOrDefault(s => s.PlatformName.Equals(integration.PlatformName, StringComparison.OrdinalIgnoreCase));
        if (cmsService == null)
        {
            return new DeployRecommendationResult(false, $"{integration.PlatformName} publishing is not supported yet.", null);
        }

        var prompt = $"Create a blog post addressing this SEO recommendation: Title: {recommendation.Title}, Description: {recommendation.Description}";
        var completion = await _aiCompletionService.CompleteAsync(
            request.OrganizationId,
            "deployment.recommendation_content",
            prompt,
            "You are an expert SEO content writer. Return only the publishable content body in Markdown.",
            requireJson: false,
            preferredProviderKey: "openai",
            cancellationToken);
        if (!completion.Success)
        {
            return new DeployRecommendationResult(false, completion.ErrorMessage ?? "Recommendation content generation failed.", null);
        }

        try
        {
            var deployedUrl = await cmsService.DeployContentAsync(integration, recommendation.Title, completion.Content, request.Status);

            await _websiteRepository.UpdateRecommendationStatusAsync(recommendation.Id, "Deployed", deployedUrl);

            return new DeployRecommendationResult(true, "Deployed successfully.", deployedUrl);
        }
        catch (Exception ex)
        {
            await _websiteRepository.UpdateRecommendationStatusAsync(recommendation.Id, "Failed", null);
            return new DeployRecommendationResult(false, $"Deploy failed: {ex.Message}", null);
        }
    }
}
