using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Competitors;

/// <summary>
/// Smart cache service that validates whether existing competitor data
/// is still fresh relative to the business profile.
/// </summary>
public interface ICompetitorCacheService
{
    /// <summary>
    /// Returns cached competitors if the business profile hasn't changed since the last discovery.
    /// </summary>
    Task<(bool IsValid, IEnumerable<Competitor>? Competitors)> TryGetCachedAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates the competitor cache by deleting all competitors for an organization.
    /// </summary>
    Task InvalidateCacheAsync(Guid organizationId, CancellationToken cancellationToken);
}
