using System.Security.Claims;
using Citationly.Application.Interfaces;

namespace Citationly.API.Services;

public sealed class CurrentOrganizationAccessor : ICurrentOrganizationAccessor
{
    private readonly IUserRepository _userRepository;

    public CurrentOrganizationAccessor(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<Guid?> GetOrganizationIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var firebaseUid = user.FindFirst("user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            return null;
        }

        var applicationUser = await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
        return applicationUser?.OrganizationId;
    }
}
