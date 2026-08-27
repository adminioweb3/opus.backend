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
        var currentUser = await GetCurrentUserAsync(user, cancellationToken);
        return currentUser?.OrganizationId;
    }

    public async Task<(Guid UserId, Guid OrganizationId, string Role)?> GetCurrentUserAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var firebaseUid = user.FindFirst("user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            return null;
        }

        return await _userRepository.GetUserByFirebaseUidAsync(firebaseUid);
    }
}
