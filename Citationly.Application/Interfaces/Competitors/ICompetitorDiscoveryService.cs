using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorDiscoveryService
{
    /// <summary>
    /// Ranks the org's own Company against real candidates already in the Company Knowledge
    /// Graph (via internal cosine-similarity search) — never invents a company. Returns up to
    /// the top 20 CompanyCompetitor edges, each with a real Similarity score plus an AI-written
    /// Confidence/Rank/Reason/Strength/Weakness. Also promotes any company that actually
    /// co-occurs with the brand in the org's real AI response history (PromptMentions) to the
    /// top of the list with DiscoverySource="observed" - real behavioral evidence outranks both
    /// embedding similarity and LLM guesses.
    /// </summary>
    Task<List<CompanyCompetitor>> DiscoverCompetitorsAsync(
        Guid organizationId,
        Guid companyId,
        string businessName,
        string rawProfileJson,
        CancellationToken cancellationToken);
}
