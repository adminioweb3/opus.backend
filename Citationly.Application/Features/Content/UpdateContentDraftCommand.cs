using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Content;

public class UpdateContentDraftCommand : IRequest<ContentDraft?>
{
    public Guid OrganizationId { get; set; }
    public Guid DraftId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class UpdateContentDraftCommandHandler : IRequestHandler<UpdateContentDraftCommand, ContentDraft?>
{
    private readonly IContentDraftRepository _repository;

    public UpdateContentDraftCommandHandler(IContentDraftRepository repository)
    {
        _repository = repository;
    }

    public async Task<ContentDraft?> Handle(UpdateContentDraftCommand request, CancellationToken cancellationToken)
    {
        var draft = await _repository.GetByIdAsync(request.DraftId);
        if (draft == null || draft.OrganizationId != request.OrganizationId) return null;

        draft.Content = request.Content;
        draft.WordCount = request.Content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (!string.IsNullOrWhiteSpace(request.Status)) draft.Status = request.Status;

        await _repository.UpdateAsync(draft);
        return draft;
    }
}
