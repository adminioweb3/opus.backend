using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Competitors;

public interface ICompetitorEnrichmentService
{
    /// <summary>
    /// Enriches a single competitor with detailed intelligence via a focused AI call.
    /// Updates the competitor's EnrichedJson, EnrichmentStatus, and EnrichedAt fields.
    /// </summary>
    Task EnrichCompetitorAsync(Competitor competitor, string businessProfileJson, CancellationToken cancellationToken);
}
