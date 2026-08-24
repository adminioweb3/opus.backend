using System.Security.Claims;

namespace Citationly.API.Services;

/// <summary>
/// Resolves the authenticated caller's organization on the server. Tenant IDs from routes,
/// query strings, and request bodies must never be used to authorize data access.
/// </summary>
public interface ICurrentOrganizationAccessor
{
    Task<Guid?> GetOrganizationIdAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
