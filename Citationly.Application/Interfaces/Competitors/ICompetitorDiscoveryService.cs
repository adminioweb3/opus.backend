using Citationly.Application.Features.Onboarding;

namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorDiscoveryService
{
    /// <summary>
    /// Executes the hybrid competitor discovery pipeline to return the top 40-50 ranked competitors.
    /// </summary>
    Task<List<CompCompetitor>> DiscoverCompetitorsAsync(
        string rawProfileJson,
        string websiteUrl,
        string businessName,
        Guid organizationId,
        CancellationToken cancellationToken);
}
