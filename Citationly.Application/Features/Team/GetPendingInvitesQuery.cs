using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class GetPendingInvitesQuery : IRequest<List<TeamInviteDto>>
{
    public Guid OrganizationId { get; set; }
}

public class GetPendingInvitesQueryHandler : IRequestHandler<GetPendingInvitesQuery, List<TeamInviteDto>>
{
    private readonly ITeamRepository _repository;

    public GetPendingInvitesQueryHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<List<TeamInviteDto>> Handle(GetPendingInvitesQuery request, CancellationToken cancellationToken)
        => _repository.GetPendingInvitesByOrgAsync(request.OrganizationId);
}
