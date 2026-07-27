using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services.Competitors;

/// <summary>
/// Smart cache validation service. Cache is valid only when the org already has Competitors
/// rows AND its Company Knowledge Graph node was analyzed within the last 30 days — the same
/// staleness clock CompanyGraphService uses to decide whether to refresh a Company.
/// </summary>
public class CompetitorCacheService : ICompetitorCacheService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(30);

    private readonly IWebsiteRepository _websiteRepository;
    private readonly ICompanyRepository _companyRepository;

    public CompetitorCacheService(IWebsiteRepository websiteRepository, ICompanyRepository companyRepository)
    {
        _websiteRepository = websiteRepository;
        _companyRepository = companyRepository;
    }

    public async Task<(bool IsValid, IEnumerable<Competitor>? Competitors)> TryGetCachedAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        int count = await _websiteRepository.GetCompetitorCountAsync(organizationId);
        if (count == 0)
            return (false, null);

        var website = (await _websiteRepository.GetWebsitesByOrgAsync(organizationId))
            .FirstOrDefault(w => w.CompanyId.HasValue);
        if (website?.CompanyId == null)
            return (false, null);

        var company = await _companyRepository.GetByIdAsync(website.CompanyId.Value);
        if (company == null || DateTime.UtcNow - company.LastAnalyzedAt > StaleAfter)
        {
            Console.WriteLine($"[Cache] Stale or missing Company node for org {organizationId}");
            return (false, null);
        }

        var competitorList = (await _websiteRepository.GetCompetitorsAsync(organizationId)).ToList();
        if (!competitorList.Any())
            return (false, null);

        Console.WriteLine($"[Cache] Valid: Returning {competitorList.Count} cached competitors");
        return (true, competitorList);
    }

    public async Task InvalidateCacheAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await _websiteRepository.DeleteCompetitorsByOrgAsync(organizationId);
        Console.WriteLine($"[Cache] Invalidated for org {organizationId}");
    }
}
