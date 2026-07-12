using MediatR;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.SourceFolders;

public class CreateSourceFolderCommand : IRequest<SourceFolder>
{
    public Guid KnowledgeBaseId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CreateSourceFolderCommandHandler : IRequestHandler<CreateSourceFolderCommand, SourceFolder>
{
    private readonly ISourceFolderRepository _repository;

    public CreateSourceFolderCommandHandler(ISourceFolderRepository repository)
    {
        _repository = repository;
    }

    public async Task<SourceFolder> Handle(CreateSourceFolderCommand request, CancellationToken cancellationToken)
    {
        var folder = new SourceFolder
        {
            KnowledgeBaseId = request.KnowledgeBaseId,
            Name = request.Name
        };

        return await _repository.CreateAsync(folder);
    }
}
