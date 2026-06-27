using Citationly.Domain.Entities;

namespace Citationly.Application.Interfaces;

public interface IWebsiteRepository
{
    Task<Guid> GetOrInsertWebsiteAsync(Guid organizationId, string domainUrl);
    Task<IEnumerable<Website>> GetAllWebsitesAsync();
    Task<IEnumerable<Website>> GetWebsitesByOrgAsync(Guid organizationId);
    Task<Website> ConnectWebsiteAsync(Guid organizationId, string domainUrl, string platformName);
    Task<Guid> InsertCrawledPageAsync(CrawledPage page);
    Task<Guid> InsertRecommendationAsync(Recommendation recommendation);
    Task<Guid> InsertWebsiteProfileAsync(WebsiteProfile profile);
    Task<WebsiteProfile?> GetLatestWebsiteProfileAsync(Guid organizationId);
    Task InsertCompetitorsAsync(IEnumerable<Competitor> competitors);
    Task<IEnumerable<Competitor>> GetCompetitorsAsync(Guid organizationId);
    Task<int> GetCompetitorCountAsync(Guid organizationId);
    Task<int> GetAiSearchPromptCountAsync(Guid organizationId);
    Task InsertAiSearchPromptsAsync(IEnumerable<AiSearchPrompt> prompts);
    Task<IEnumerable<AiSearchPrompt>> GetAiSearchPromptsAsync(Guid organizationId);
    Task UpdateAiSearchPromptsVisibilityAsync(IEnumerable<AiSearchPrompt> prompts);
    Task InsertPlatformVisibilityAsync(VisibilitySummary summary, IEnumerable<PlatformVisibility> visibilities);
    Task<VisibilitySummary?> GetVisibilitySummaryAsync(Guid organizationId);
    Task<IEnumerable<PlatformVisibility>> GetPlatformVisibilitiesAsync(Guid organizationId);
    Task InsertCitationsAsync(CitationSummary summary, IEnumerable<CitationSource> sources);
    Task<CitationSummary?> GetCitationSummaryAsync(Guid organizationId);
    Task<IEnumerable<CitationSource>> GetCitationSourcesAsync(Guid organizationId);
    Task InsertPersonaAnalysisAsync(PersonaAnalysisSummary summary, IEnumerable<PersonaScore> scores);
    Task<PersonaAnalysisSummary?> GetPersonaAnalysisSummaryAsync(Guid organizationId);
    Task<IEnumerable<PersonaScore>> GetPersonaScoresAsync(Guid organizationId);
    Task InsertRegionAnalysisAsync(RegionAnalysisSummary summary, IEnumerable<RegionScore> scores);
    Task<RegionAnalysisSummary?> GetRegionAnalysisSummaryAsync(Guid organizationId);
    Task<IEnumerable<RegionScore>> GetRegionScoresAsync(Guid organizationId);
    Task InsertGeoRecommendationsAsync(GeoRecommendationSummary summary, IEnumerable<GeoRecommendation> recommendations);
    Task<GeoRecommendationSummary?> GetGeoRecommendationSummaryAsync(Guid organizationId);
    Task<IEnumerable<GeoRecommendation>> GetGeoRecommendationsAsync(Guid organizationId);
    Task InsertExecutiveSummaryAsync(ExecutiveSummaryData summary);
    Task<ExecutiveSummaryData?> GetExecutiveSummaryAsync(Guid organizationId);
}
