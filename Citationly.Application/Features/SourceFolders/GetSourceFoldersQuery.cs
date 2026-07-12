using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.SourceFolders;

public class GetSourceFoldersQuery : IRequest<IEnumerable<SourceFolder>>
{
    public Guid KnowledgeBaseId { get; set; }
}

public class GetSourceFoldersQueryHandler : IRequestHandler<GetSourceFoldersQuery, IEnumerable<SourceFolder>>
{
    private readonly ISourceFolderRepository _repository;

    public GetSourceFoldersQueryHandler(ISourceFolderRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<SourceFolder>> Handle(GetSourceFoldersQuery request, CancellationToken cancellationToken)
    {
        return await _repository.GetByKnowledgeBaseAsync(request.KnowledgeBaseId);
    }
}
