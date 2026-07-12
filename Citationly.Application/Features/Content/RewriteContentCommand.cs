using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.Content;

public class RewriteContentCommand : IRequest<ContentDraft?>
{
    public Guid OrganizationId { get; set; }
    public Guid DraftId { get; set; }
    public string Instruction { get; set; } = string.Empty;
}

public class RewriteContentCommandHandler : IRequestHandler<RewriteContentCommand, ContentDraft?>
{
    private readonly IContentDraftRepository _repository;
    private readonly IOpenAiService _openAiService;

    public RewriteContentCommandHandler(IContentDraftRepository repository, IOpenAiService openAiService)
    {
        _repository = repository;
        _openAiService = openAiService;
    }

    public async Task<ContentDraft?> Handle(RewriteContentCommand request, CancellationToken cancellationToken)
    {
        var draft = await _repository.GetByIdAsync(request.DraftId);
        if (draft == null || draft.OrganizationId != request.OrganizationId) return null;

        const string systemPrompt =
            "You are an expert editor. Rewrite the given content according to the instruction, preserving its Markdown " +
            "structure and its H1 title unless the instruction says otherwise. Output only the rewritten content, no commentary.";

        var prompt = $"INSTRUCTION: {request.Instruction}\n\nCONTENT TO REWRITE:\n{draft.Content}";

        var rewritten = await _openAiService.GenerateContentAsync(prompt, systemPrompt);

        draft.Content = rewritten;
        draft.WordCount = rewritten.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

        await _repository.UpdateAsync(draft);
        return draft;
    }
}
