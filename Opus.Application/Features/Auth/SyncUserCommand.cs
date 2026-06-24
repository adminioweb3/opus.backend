using MediatR;
using Opus.Application.Interfaces;

namespace Opus.Application.Features.Auth;

public class SyncUserCommand : IRequest<SyncUserResult>
{
    public string FirebaseUid { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class SyncUserResult
{
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Role { get; set; } = string.Empty;
}

public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    private readonly IUserRepository _repository;

    public SyncUserCommandHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<SyncUserResult> Handle(SyncUserCommand request, CancellationToken cancellationToken)
    {
        var result = await _repository.CreateOrGetUserAsync(request.FirebaseUid, request.Email, request.DisplayName);

        return new SyncUserResult
        {
            UserId = result.UserId,
            OrganizationId = result.OrganizationId,
            Role = result.Role
        };
    }
}
