using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Content;

public class GetPublishingSummaryQuery : IRequest<PublishingSummaryDto>
{
    public Guid OrganizationId { get; set; }
}

public record PublishingSummaryDto(
    int TotalDrafts,
    int DraftCount,
    int OptimizedCount,
    int PublishedCount,
    int FailedCount,
    int PublishedToday,
    int ConnectedIntegrations);

public class GetPublishingSummaryQueryHandler : IRequestHandler<GetPublishingSummaryQuery, PublishingSummaryDto>
{
    private readonly IContentDraftRepository _draftRepository;
    private readonly IIntegrationRepository _integrationRepository;

    public GetPublishingSummaryQueryHandler(IContentDraftRepository draftRepository, IIntegrationRepository integrationRepository)
    {
        _draftRepository = draftRepository;
        _integrationRepository = integrationRepository;
    }

    public async Task<PublishingSummaryDto> Handle(GetPublishingSummaryQuery request, CancellationToken cancellationToken)
    {
        var drafts = await _draftRepository.GetByOrgAsync(request.OrganizationId);
        var integrations = await _integrationRepository.GetIntegrationsByOrgAsync(request.OrganizationId);
        var today = DateTime.UtcNow.Date;

        return new PublishingSummaryDto(
            TotalDrafts: drafts.Count,
            DraftCount: drafts.Count(d => d.Status == "Draft"),
            OptimizedCount: drafts.Count(d => d.Status == "Optimized"),
            PublishedCount: drafts.Count(d => d.Status == "Published"),
            FailedCount: drafts.Count(d => d.Status == "Failed"),
            PublishedToday: drafts.Count(d => d.Status == "Published" && d.PublishedAt.HasValue && d.PublishedAt.Value.Date == today),
            ConnectedIntegrations: integrations.Count());
    }
}
