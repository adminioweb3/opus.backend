using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class UpdateMemberRoleCommand : IRequest<bool>
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class UpdateMemberRoleCommandHandler : IRequestHandler<UpdateMemberRoleCommand, bool>
{
    private readonly ITeamRepository _repository;

    public UpdateMemberRoleCommandHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<bool> Handle(UpdateMemberRoleCommand request, CancellationToken cancellationToken)
        => _repository.UpdateMemberRoleAsync(request.OrganizationId, request.UserId, request.Role);
}
