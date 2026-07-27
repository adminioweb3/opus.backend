using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorDiscoveryService
{
    /// <summary>
    /// Ranks the org's own Company against real candidates already in the Company Knowledge
    /// Graph (via internal cosine-similarity search) — never invents a company. Returns up to
    /// the top 20 CompanyCompetitor edges, each with a real Similarity score plus an AI-written
    /// Confidence/Rank/Reason/Strength/Weakness.
    /// </summary>
    Task<List<CompanyCompetitor>> DiscoverCompetitorsAsync(
        Guid companyId,
        string businessName,
        string rawProfileJson,
        CancellationToken cancellationToken);
}
