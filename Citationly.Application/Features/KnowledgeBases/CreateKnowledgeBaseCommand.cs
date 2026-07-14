using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.KnowledgeBases;

public class CreateKnowledgeBaseCommand : IRequest<KnowledgeBase>
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "Building2";
    public string Tint { get; set; } = "#6366F1";
    public string Bg { get; set; } = "#EEEEFE";
    public string? Description { get; set; }
}

public class CreateKnowledgeBaseCommandHandler : IRequestHandler<CreateKnowledgeBaseCommand, KnowledgeBase>
{
    private readonly IKnowledgeBaseRepository _repository;

    public CreateKnowledgeBaseCommandHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<KnowledgeBase> Handle(CreateKnowledgeBaseCommand request, CancellationToken cancellationToken)
    {
        var knowledgeBase = new KnowledgeBase
        {
            OrganizationId = request.OrganizationId,
            Name = request.Name,
            Icon = request.Icon,
            Tint = request.Tint,
            Bg = request.Bg,
            Description = request.Description
        };

        return await _repository.CreateAsync(knowledgeBase);
    }
}
