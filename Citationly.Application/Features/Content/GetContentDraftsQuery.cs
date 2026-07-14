using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Content;

public class GetContentDraftsQuery : IRequest<List<ContentDraft>>
{
    public Guid OrganizationId { get; set; }
}

public class GetContentDraftsQueryHandler : IRequestHandler<GetContentDraftsQuery, List<ContentDraft>>
{
    private readonly IContentDraftRepository _repository;

    public GetContentDraftsQueryHandler(IContentDraftRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<ContentDraft>> Handle(GetContentDraftsQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetByOrgAsync(request.OrganizationId);
    }
}

public class GetContentDraftQuery : IRequest<ContentDraft?>
{
    public Guid OrganizationId { get; set; }
    public Guid DraftId { get; set; }
}

public class GetContentDraftQueryHandler : IRequestHandler<GetContentDraftQuery, ContentDraft?>
{
    private readonly IContentDraftRepository _repository;

    public GetContentDraftQueryHandler(IContentDraftRepository repository)
    {
        _repository = repository;
    }

    public async Task<ContentDraft?> Handle(GetContentDraftQuery request, CancellationToken cancellationToken)
    {
        var draft = await _repository.GetByIdAsync(request.DraftId);
        if (draft == null || draft.OrganizationId != request.OrganizationId) return null;
        return draft;
    }
}
