using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class GetTeamMembersQuery : IRequest<List<TeamMemberDto>>
{
    public Guid OrganizationId { get; set; }
}

public class GetTeamMembersQueryHandler : IRequestHandler<GetTeamMembersQuery, List<TeamMemberDto>>
{
    private readonly ITeamRepository _repository;

    public GetTeamMembersQueryHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<List<TeamMemberDto>> Handle(GetTeamMembersQuery request, CancellationToken cancellationToken)
        => _repository.GetMembersByOrgAsync(request.OrganizationId);
}
