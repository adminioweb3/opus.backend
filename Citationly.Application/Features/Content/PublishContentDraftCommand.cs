using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Content;

public class PublishContentDraftCommand : IRequest<PublishContentResult>
{
    public Guid OrganizationId { get; set; }
    public Guid DraftId { get; set; }
}

public record PublishContentResult(bool Success, string Message, string? PublishedUrl);

public class PublishContentDraftCommandHandler : IRequestHandler<PublishContentDraftCommand, PublishContentResult>
{
    private const string SupportedPlatform = "WordPress";

    private readonly IContentDraftRepository _draftRepository;
    private readonly IIntegrationRepository _integrationRepository;
    private readonly IEnumerable<ICmsIntegrationService> _cmsServices;

    public PublishContentDraftCommandHandler(
        IContentDraftRepository draftRepository,
        IIntegrationRepository integrationRepository,
        IEnumerable<ICmsIntegrationService> cmsServices)
    {
        _draftRepository = draftRepository;
        _integrationRepository = integrationRepository;
        _cmsServices = cmsServices;
    }

    public async Task<PublishContentResult> Handle(PublishContentDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await _draftRepository.GetByIdAsync(request.DraftId);
        if (draft == null || draft.OrganizationId != request.OrganizationId)
        {
            return new PublishContentResult(false, "Draft not found.", null);
        }

        var integration = await _integrationRepository.GetIntegrationByOrgAndPlatformAsync(request.OrganizationId, SupportedPlatform);
        if (integration == null || string.IsNullOrWhiteSpace(integration.ApiUrl) || string.IsNullOrWhiteSpace(integration.ApiKey))
        {
            return new PublishContentResult(false, "No WordPress site connected yet. Connect one first.", null);
        }

        var cmsService = _cmsServices.FirstOrDefault(s => s.PlatformName.Equals(integration.PlatformName, StringComparison.OrdinalIgnoreCase));
        if (cmsService == null)
        {
            return new PublishContentResult(false, $"{integration.PlatformName} publishing is not supported yet.", null);
        }

        try
        {
            var publishedUrl = await cmsService.DeployContentAsync(integration, draft.Title, draft.Content, "publish");

            draft.Status = "Published";
            draft.PublishedUrl = publishedUrl;
            draft.PublishedAt = DateTime.UtcNow;
            draft.IntegrationId = integration.Id;
            draft.PublishError = null;
            await _draftRepository.UpdateAsync(draft);

            return new PublishContentResult(true, "Published to WordPress successfully.", publishedUrl);
        }
        catch (Exception ex)
        {
            draft.Status = "Failed";
            draft.PublishError = ex.Message;
            await _draftRepository.UpdateAsync(draft);

            return new PublishContentResult(false, $"Publish failed: {ex.Message}", null);
        }
    }
}
