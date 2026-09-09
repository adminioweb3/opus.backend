using Citationly.Application.Features.Deployments;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public class ExportDeveloperHandoffCommandTests
{
    [Fact]
    public async Task Handle_ReturnsNotFound_WhenRecommendationIsOutsideOrganization()
    {
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var repository = new StubWebsiteRepository(new Recommendation
        {
            Id = recommendationId,
            WebsiteId = Guid.NewGuid(),
            CrawledPageId = Guid.NewGuid(),
            Title = "Add FAQ coverage",
            ActionType = "FAQ gap",
            Priority = "High",
            Description = "AI answers miss this page."
        }, otherOrganizationId);

        var handler = new ExportDeveloperHandoffCommandHandler(repository);

        var result = await handler.Handle(new ExportDeveloperHandoffCommand
        {
            OrganizationId = organizationId,
            RecommendationId = recommendationId,
            SiteType = "nextjs-app-router"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Recommendation not found.", result.Message);
        Assert.Null(result.Markdown);
    }

    [Fact]
    public async Task Handle_GeneratesNextJsDeveloperHandoff_WithReviewGuardrails()
    {
        var organizationId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var repository = new StubWebsiteRepository(new Recommendation
        {
            Id = recommendationId,
            WebsiteId = Guid.NewGuid(),
            CrawledPageId = Guid.NewGuid(),
            Title = "Add buyer FAQ section",
            ActionType = "FAQ gap",
            Priority = "High",
            Status = "New",
            Description = "Competitor pages answer implementation questions more clearly."
        }, organizationId);

        var handler = new ExportDeveloperHandoffCommandHandler(repository);

        var result = await handler.Handle(new ExportDeveloperHandoffCommand
        {
            OrganizationId = organizationId,
            RecommendationId = recommendationId,
            SiteType = "nextjs-app-router",
            TargetPath = "app/solutions/saas/page.tsx",
            DeliveryOption = "Export patch"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("citationly-add-buyer-faq-section-developer-handoff.md", result.FileName);
        Assert.Contains("app/solutions/saas/page.tsx", result.Markdown);
        Assert.Contains("Use an existing reusable FAQ/accordion/content component", result.Markdown);
        Assert.Contains("do not auto-publish AI-generated copy without approval", result.Markdown);
        Assert.Contains("After deploy, re-scan the affected URL/prompts", result.Markdown);
    }

    [Fact]
    public async Task Handle_RejectsUnsupportedSiteTypes()
    {
        var organizationId = Guid.NewGuid();
        var recommendationId = Guid.NewGuid();
        var repository = new StubWebsiteRepository(new Recommendation
        {
            Id = recommendationId,
            WebsiteId = Guid.NewGuid(),
            CrawledPageId = Guid.NewGuid(),
            Title = "Add schema",
            ActionType = "schema gap"
        }, organizationId);

        var handler = new ExportDeveloperHandoffCommandHandler(repository);

        var result = await handler.Handle(new ExportDeveloperHandoffCommand
        {
            OrganizationId = organizationId,
            RecommendationId = recommendationId,
            SiteType = "unknown-framework"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Message);
    }

    private sealed class StubWebsiteRepository : IWebsiteRepository
    {
        private readonly Recommendation _recommendation;
        private readonly Guid _organizationId;

        public StubWebsiteRepository(Recommendation recommendation, Guid organizationId)
        {
            _recommendation = recommendation;
            _organizationId = organizationId;
        }

        public Task<Recommendation?> GetRecommendationByIdAsync(Guid id, Guid organizationId)
        {
            return Task.FromResult(id == _recommendation.Id && organizationId == _organizationId ? _recommendation : null);
        }

        public Task<Guid> GetOrInsertWebsiteAsync(Guid organizationId, string domainUrl) => throw new NotImplementedException();
        public Task<IEnumerable<Website>> GetAllWebsitesAsync() => throw new NotImplementedException();
        public Task<IEnumerable<Website>> GetWebsitesByOrgAsync(Guid organizationId) => throw new NotImplementedException();
        public Task LinkWebsiteToCompanyAsync(Guid websiteId, Guid companyId) => throw new NotImplementedException();
        public Task<Website> ConnectWebsiteAsync(Guid organizationId, string domainUrl, string platformName) => throw new NotImplementedException();
        public Task<Guid> InsertCrawledPageAsync(CrawledPage page) => throw new NotImplementedException();
        public Task<Guid> InsertRecommendationAsync(Recommendation recommendation) => throw new NotImplementedException();
        public Task UpdateRecommendationStatusAsync(Guid id, string status, string? deployedUrl) => throw new NotImplementedException();
        public Task<Guid> InsertWebsiteProfileAsync(WebsiteProfile profile) => throw new NotImplementedException();
        public Task<WebsiteProfile?> GetLatestWebsiteProfileAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertCompetitorsAsync(IEnumerable<Competitor> competitors) => throw new NotImplementedException();
        public Task<IEnumerable<Competitor>> GetCompetitorsAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<int> GetCompetitorCountAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<int> GetAiSearchPromptCountAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertAiSearchPromptsAsync(IEnumerable<AiSearchPrompt> prompts) => throw new NotImplementedException();
        public Task<IEnumerable<AiSearchPrompt>> GetAiSearchPromptsAsync(Guid organizationId) => throw new NotImplementedException();
        public Task UpdateAiSearchPromptsVisibilityAsync(IEnumerable<AiSearchPrompt> prompts) => throw new NotImplementedException();
        public Task UpdateAiSearchPromptsAsync(IEnumerable<AiSearchPrompt> prompts) => throw new NotImplementedException();
        public Task DeleteAiSearchPromptsAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertPlatformVisibilityAsync(VisibilitySummary summary, IEnumerable<PlatformVisibility> visibilities) => throw new NotImplementedException();
        public Task UpdatePlatformVisibilityAsync(PlatformVisibility platformVisibility) => throw new NotImplementedException();
        public Task<VisibilitySummary?> GetVisibilitySummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<IEnumerable<PlatformVisibility>> GetPlatformVisibilitiesAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertCitationsAsync(CitationSummary summary, IEnumerable<CitationSource> sources) => throw new NotImplementedException();
        public Task<CitationSummary?> GetCitationSummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<IEnumerable<CitationSource>> GetCitationSourcesAsync(Guid organizationId) => throw new NotImplementedException();
        public Task UpdateCitationSourcesAsync(IEnumerable<CitationSource> sources) => throw new NotImplementedException();
        public Task<IEnumerable<CitationSource>> GetCitationsForEnrichmentAsync(Guid organizationId, int limit) => throw new NotImplementedException();
        public Task UpdateCitationSummaryAsync(CitationSummary summary) => throw new NotImplementedException();
        public Task InsertPersonaAnalysisAsync(PersonaAnalysisSummary summary, IEnumerable<PersonaScore> scores) => throw new NotImplementedException();
        public Task<PersonaAnalysisSummary?> GetPersonaAnalysisSummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<IEnumerable<PersonaScore>> GetPersonaScoresAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertRegionAnalysisAsync(RegionAnalysisSummary summary, IEnumerable<RegionScore> scores) => throw new NotImplementedException();
        public Task<RegionAnalysisSummary?> GetRegionAnalysisSummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<IEnumerable<RegionScore>> GetRegionScoresAsync(Guid organizationId) => throw new NotImplementedException();
        public Task InsertGeoRecommendationsAsync(GeoRecommendationSummary summary, IEnumerable<GeoRecommendation> recommendations) => throw new NotImplementedException();
        public Task<GeoRecommendationSummary?> GetGeoRecommendationSummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<IEnumerable<GeoRecommendation>> GetGeoRecommendationsAsync(Guid organizationId) => throw new NotImplementedException();
        public Task UpdateGeoRecommendationAsync(GeoRecommendation recommendation) => throw new NotImplementedException();
        public Task<IEnumerable<GeoRecommendation>> GetGeoRecommendationsForEnrichmentAsync(Guid organizationId, int limit) => throw new NotImplementedException();
        public Task InsertExecutiveSummaryAsync(ExecutiveSummaryData summary) => throw new NotImplementedException();
        public Task<ExecutiveSummaryData?> GetExecutiveSummaryAsync(Guid organizationId) => throw new NotImplementedException();
        public Task UpdateCompetitorAsync(Competitor competitor) => throw new NotImplementedException();
        public Task DeleteCompetitorsByOrgAsync(Guid organizationId) => throw new NotImplementedException();
        public Task<Competitor?> GetCompetitorByIdAsync(Guid competitorId) => throw new NotImplementedException();
    }
}
