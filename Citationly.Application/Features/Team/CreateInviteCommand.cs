using MediatR;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Features.Team;

public class CreateInviteCommand : IRequest<TeamInviteDto>
{
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Viewer";
    public Guid InvitedByUserId { get; set; }
}

public class CreateInviteCommandHandler : IRequestHandler<CreateInviteCommand, TeamInviteDto>
{
    private readonly ITeamRepository _repository;

    public CreateInviteCommandHandler(ITeamRepository repository)
    {
        _repository = repository;
    }

    public Task<TeamInviteDto> Handle(CreateInviteCommand request, CancellationToken cancellationToken)
        => _repository.CreateInviteAsync(request.OrganizationId, request.Email.Trim().ToLowerInvariant(), request.Role, request.InvitedByUserId);
}
