using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.KnowledgeBases;

public class GetKnowledgeBasesQuery : IRequest<IEnumerable<KnowledgeBase>>
{
    public Guid OrganizationId { get; set; }
}

public class GetKnowledgeBasesQueryHandler : IRequestHandler<GetKnowledgeBasesQuery, IEnumerable<KnowledgeBase>>
{
    private readonly IKnowledgeBaseRepository _repository;

    public GetKnowledgeBasesQueryHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<KnowledgeBase>> Handle(GetKnowledgeBasesQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetByOrgAsync(request.OrganizationId);
    }
}
