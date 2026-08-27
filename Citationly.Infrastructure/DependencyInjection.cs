using Microsoft.Extensions.DependencyInjection;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Application.Interfaces.Competitors;
using Citationly.Application.Interfaces.GeoAudit;
using Citationly.Infrastructure.Data;
using Citationly.Infrastructure.Database;
using Citationly.Infrastructure.Repositories;
using Citationly.Infrastructure.Services;
using Citationly.Infrastructure.Services.Companies;
using Citationly.Infrastructure.Services.GeoAudit;
using Citationly.Infrastructure.Services.Scraping;

namespace Citationly.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        Dapper.SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        Dapper.SqlMapper.AddTypeHandler(new NullableDateOnlyTypeHandler());

        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<DatabaseMigrationRunner>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<ITeamRepository, TeamRepository>();
        services.AddTransient<IWebsiteRepository, WebsiteRepository>();
        services.AddTransient<IIntegrationRepository, IntegrationRepository>();
        services.AddTransient<IApiKeyRepository, ApiKeyRepository>();
        services.AddTransient<IAlertRepository, AlertRepository>();
        services.AddTransient<IAuditLogRepository, AuditLogRepository>();
        services.AddTransient<IAgencyRepository, AgencyRepository>();
        services.AddTransient<ISsoRepository, SsoRepository>();
        services.AddTransient<IDataLifecycleRepository, DataLifecycleRepository>();
        services.AddTransient<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
        services.AddTransient<ISourceFolderRepository, SourceFolderRepository>();
        services.AddTransient<IContentDraftRepository, ContentDraftRepository>();
        services.AddTransient<IContentOptimizationRepository, ContentOptimizationRepository>();
        services.AddTransient<IEmbeddingRepository, EmbeddingRepository>();
        services.AddTransient<IPromptIntelligenceRepository, PromptIntelligenceRepository>();
        services.AddScoped<IMetricsRepository, MetricsRepository>();
        services.AddScoped<IScrapingJobRepository, ScrapingJobRepository>();
        services.AddScoped<IAiVisibilityRepository, AiVisibilityRepository>();
        services.AddScoped<ICompetitorSnapshotRepository, CompetitorSnapshotRepository>();
        services.AddScoped<IVisibilitySnapshotRepository, VisibilitySnapshotRepository>();
        services.AddScoped<ICitationScanSnapshotRepository, CitationScanSnapshotRepository>();
        services.AddScoped<IBrandPulseSnapshotRepository, BrandPulseSnapshotRepository>();
        services.AddScoped<ICommandCenterInsightRepository, CommandCenterInsightRepository>();
        services.AddScoped<IOpportunitySnapshotRepository, OpportunitySnapshotRepository>();
        services.AddScoped<IAnalysisRepository, AnalysisRepository>();
        services.AddScoped<IAnalysisOrchestrator, Citationly.Application.Features.AnalysisEngine.AnalysisOrchestrator>();
        services.AddScoped<IAiAnalysisService, WebsiteAiAnalysisService>();
        services.AddScoped<IMarkdownGeneratorService, MarkdownGeneratorService>();
        services.AddScoped<IScraperEngine, PlaywrightScraperEngine>();
        services.AddScoped<IScrapingJobService, ScrapingJobService>();
        services.AddScoped<IAiVisibilityEngineService, AiVisibilityEngineService>();
        services.AddSingleton<Citationly.Application.Interfaces.IAiRequestContextAccessor, AiRequestContextAccessor>();
        services.AddSingleton<Citationly.Application.Interfaces.IAiRateLimitStore, AiRateLimitStore>();
        services.AddSingleton<Citationly.Application.Interfaces.IAiUsageLimiter, AiUsageLimiter>();
        services.AddSingleton<Citationly.Application.Interfaces.IAiResilienceService, AiResilienceService>();
        // Singleton (not Scoped): it only opens per-call connections via the already-singleton
        // IDbConnectionFactory and holds no per-request state, so it can be safely injected into
        // the singleton AiUsageLimiter without becoming a captive dependency.
        services.AddSingleton<Citationly.Application.Interfaces.IEntitlementService, EntitlementService>();
        services.AddScoped<Citationly.Application.Interfaces.IBillingRepository, Citationly.Infrastructure.Repositories.BillingRepository>();
        services.AddScoped<Citationly.Application.Interfaces.IBillingService, StripeBillingService>();
        
        // Company Knowledge Graph
        services.AddTransient<ICompanyRepository, CompanyRepository>();
        services.AddTransient<ICompanyCompetitorRepository, CompanyCompetitorRepository>();
        services.AddScoped<ICompanySimilarityService, CompanySimilarityService>();
        services.AddScoped<ICompanyGraphService, CompanyGraphService>();

        // Competitors
        services.AddScoped<ICompetitorDiscoveryService, Citationly.Infrastructure.Services.Competitors.CompetitorDiscoveryService>();
        services.AddScoped<ICompetitorGraphSyncService, Citationly.Infrastructure.Services.Competitors.CompetitorGraphSyncService>();
        services.AddScoped<ICompetitorRankingService, Citationly.Infrastructure.Services.Competitors.CompetitorRankingService>();
        services.AddScoped<ICompetitorCacheService, Citationly.Infrastructure.Services.Competitors.CompetitorCacheService>();

        // Application Services
        services.AddScoped<IOpenAiService, OpenAiService>();
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingService>();

        // AI provider abstraction (Phase 2) - one real implementation per vendor. Each is
        // IsConfigured=false (and simply excluded by AiProviderRegistry.GetConfiguredProviders())
        // until its own API key is set - no provider ever fabricates another vendor's response.
        services.AddScoped<Citationly.Application.Interfaces.IAiProvider, Citationly.Infrastructure.Services.AiProviders.OpenAiProvider>();
        services.AddScoped<Citationly.Application.Interfaces.IAiProvider, Citationly.Infrastructure.Services.AiProviders.AnthropicProvider>();
        services.AddScoped<Citationly.Application.Interfaces.IAiProvider, Citationly.Infrastructure.Services.AiProviders.GoogleGeminiProvider>();
        services.AddScoped<Citationly.Application.Interfaces.IAiProvider, Citationly.Infrastructure.Services.AiProviders.PerplexityProvider>();
        services.AddScoped<Citationly.Application.Interfaces.IAiProviderRegistry, Citationly.Infrastructure.Services.AiProviders.AiProviderRegistry>();
        // Onboarding Pipeline Services
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IPageClassificationService, Citationly.Infrastructure.Services.Onboarding.PageClassificationService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IPageRankingService, Citationly.Infrastructure.Services.Onboarding.PageRankingService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IContentCleaningService, Citationly.Infrastructure.Services.Onboarding.ContentCleaningService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IWebsiteContentBuilder, Citationly.Infrastructure.Services.Onboarding.WebsiteContentBuilder>();

        // Recommendation Pipeline Services
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IGeoRecommendationCacheService, Citationly.Infrastructure.Services.Onboarding.GeoRecommendationCacheService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IGapDetectionService, Citationly.Infrastructure.Services.Onboarding.GapDetectionService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IRecommendationDiscoveryService, Citationly.Infrastructure.Services.Onboarding.RecommendationDiscoveryService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IRoadmapService, Citationly.Infrastructure.Services.Onboarding.RoadmapService>();
        services.AddScoped<Citationly.Application.Interfaces.Onboarding.IRecommendationEnrichmentQueue, Citationly.Infrastructure.Services.Onboarding.RecommendationEnrichmentQueue>();
        services.AddScoped<Citationly.Infrastructure.Services.Onboarding.RecommendationBackgroundWorker>();

        // Prompt Generation Pipeline Services
        services.AddScoped<Citationly.Application.Interfaces.Prompts.IPromptDiscoveryService, Citationly.Infrastructure.Services.Prompts.PromptDiscoveryService>();
        services.AddScoped<Citationly.Application.Interfaces.Prompts.IPromptEnrichmentService, Citationly.Infrastructure.Services.Prompts.PromptEnrichmentService>();
        services.AddScoped<Citationly.Application.Interfaces.Prompts.IPromptCacheService, Citationly.Infrastructure.Services.Prompts.PromptCacheService>();
        
        // Background Queue for Prompts (Singleton)
        services.AddSingleton<Citationly.Application.Interfaces.Prompts.IPromptBatchProcessor, Citationly.Infrastructure.Services.Prompts.PromptBatchProcessor>();
        // Hosted Background Service
        services.AddHostedService<Citationly.Infrastructure.Services.Prompts.PromptBackgroundWorker>();

        // Visibility Pipeline Services
        services.AddScoped<Citationly.Application.Interfaces.Visibility.IVisibilityScoringService, Citationly.Infrastructure.Services.Visibility.VisibilityScoringService>();
        services.AddScoped<Citationly.Application.Interfaces.Visibility.IVisibilityRankingService, Citationly.Infrastructure.Services.Visibility.VisibilityRankingService>();
        services.AddScoped<Citationly.Application.Interfaces.Visibility.IVisibilityCacheService, Citationly.Infrastructure.Services.Visibility.VisibilityCacheService>();
        services.AddScoped<Citationly.Application.Interfaces.Visibility.IPlatformInsightService, Citationly.Infrastructure.Services.Visibility.PlatformInsightService>();
        services.AddSingleton<Citationly.Application.Interfaces.Visibility.IVisibilityBatchProcessor, Citationly.Infrastructure.Services.Visibility.VisibilityBatchProcessor>();
        services.AddHostedService<Citationly.Infrastructure.Services.Visibility.VisibilityBackgroundWorker>();

        // Citation Pipeline Services
        services.AddScoped<Citationly.Application.Interfaces.Citations.ICitationDiscoveryService, Citationly.Infrastructure.Services.Citations.CitationDiscoveryService>();
        services.AddScoped<Citationly.Application.Interfaces.Citations.ICitationEnrichmentService, Citationly.Infrastructure.Services.Citations.CitationEnrichmentService>();
        services.AddScoped<Citationly.Application.Interfaces.Citations.ICitationAnalyticsService, Citationly.Infrastructure.Services.Citations.CitationAnalyticsService>();
        services.AddScoped<Citationly.Application.Interfaces.Citations.ICitationBatchProcessor, Citationly.Infrastructure.Services.Citations.CitationBatchProcessor>();
        services.AddScoped<IRecommendationImpactService, RecommendationImpactService>();
        services.AddScoped<IBrandKnowledgeService, BrandKnowledgeService>();
        services.AddScoped<ICrossEngineConsensusService, CrossEngineConsensusService>();
        services.AddScoped<IAlertService, AlertService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddScoped<Citationly.Application.Interfaces.GeoOptimizer.IGeoOptimizerService, Citationly.Infrastructure.Services.GeoOptimizer.GeoOptimizerService>();
        services.AddHttpClient<IGeoTechnicalAuditService, GeoTechnicalAuditService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
        services.AddScoped<Citationly.Application.Interfaces.AnswerSimulator.IAnswerSimulatorService, Citationly.Infrastructure.Services.AnswerSimulator.AnswerSimulatorService>();

        services.AddHttpClient<ICmsIntegrationService, WordPressIntegrationService>();
        services.AddHttpClient<IOpenAiService, OpenAiService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.GeoScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.CompetitorScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.VisibilityScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.CitationScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.BrandPulseScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.CommandCenterInsightsRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.OpportunityScanRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.RecommendationImpactRecurringJob>();
        services.AddScoped<Citationly.Infrastructure.BackgroundJobs.AlertDeliveryRecurringJob>();

        return services;
    }
}
