using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces.Citations;

public interface ICitationDiscoveryService
{
    Task<List<CitationSource>> DiscoverCitationsAsync(Guid organizationId, string websiteUrl, string websiteProfileJson, string promptAnalysisJson, string platformScoresJson);
}

public interface ICitationEnrichmentService
{
    Task<List<CitationSource>> EnrichCitationsAsync(Guid organizationId, string websiteProfileJson, List<CitationSource> sourcesToEnrich);
}

public interface ICitationAnalyticsService
{
    Task ComputeAnalyticsAsync(Guid organizationId);
}

public interface ICitationBatchProcessor
{
    Task QueueCitationEnrichmentAsync(Guid organizationId);
    Task ProcessEnrichmentBatchAsync(Guid organizationId);
}
