using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Smart cache validation service.
/// Returns cached competitors only if the business profile hasn't changed since last discovery.
/// </summary>
public class CompetitorCacheService : ICompetitorCacheService
{
    private readonly IWebsiteRepository _websiteRepository;

    public CompetitorCacheService(IWebsiteRepository websiteRepository)
    {
        _websiteRepository = websiteRepository;
    }

    public async Task<(bool IsValid, IEnumerable<Competitor>? Competitors)> TryGetCachedAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        int count = await _websiteRepository.GetCompetitorCountAsync(organizationId);
        if (count == 0)
            return (false, null);

        // Check if the business profile has been updated since the last discovery
        var profile = await _websiteRepository.GetLatestWebsiteProfileAsync(organizationId);
        if (profile == null)
            return (false, null);

        var competitors = await _websiteRepository.GetCompetitorsAsync(organizationId);
        var competitorList = competitors.ToList();

        if (!competitorList.Any())
            return (false, null);

        // If the profile was created AFTER the oldest competitor, cache is stale
        var oldestCompetitor = competitorList.Min(c => c.CreatedAt);
        if (profile.CreatedAt > oldestCompetitor)
        {
            Console.WriteLine($"[Cache] Stale: Profile ({profile.CreatedAt:u}) is newer than competitors ({oldestCompetitor:u})");
            return (false, null);
        }

        Console.WriteLine($"[Cache] Valid: Returning {competitorList.Count} cached competitors");
        return (true, competitorList);
    }

    public async Task InvalidateCacheAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await _websiteRepository.DeleteCompetitorsByOrgAsync(organizationId);
        Console.WriteLine($"[Cache] Invalidated for org {organizationId}");
    }
}
