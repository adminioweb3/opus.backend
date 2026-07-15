using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class RemoveMemberCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
}

public class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand, bool>
{
    private readonly ITeamRepository _repository;

    public RemoveMemberCommandHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> Handle(RemoveMemberCommand request, CancellationToken cancellationToken)
        => _repository.RemoveMemberAsync(request.OrganizationId, request.UserId);
}
