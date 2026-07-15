using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class RevokeInviteCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public Guid InviteId { get; set; }
}

public class RevokeInviteCommandHandler : IRequestHandler<RevokeInviteCommand, bool>
{
    private readonly ITeamRepository _repository;

    public RevokeInviteCommandHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> Handle(RevokeInviteCommand request, CancellationToken cancellationToken)
        => _repository.RevokeInviteAsync(request.OrganizationId, request.InviteId);
}
