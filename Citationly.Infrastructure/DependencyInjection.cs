using Microsoft.Extensions.DependencyInjection;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Data;
using Citationly.Infrastructure.Repositories;
using Citationly.Infrastructure.Services;
using Citationly.Infrastructure.Services.Scraping;

namespace Citationly.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IWebsiteRepository, WebsiteRepository>();
        services.AddTransient<IIntegrationRepository, IntegrationRepository>();
        services.AddTransient<IEmbeddingRepository, EmbeddingRepository>();
        services.AddTransient<IPromptIntelligenceRepository, PromptIntelligenceRepository>();
        services.AddScoped<IMetricsRepository, MetricsRepository>();
        services.AddScoped<IScrapingJobRepository, ScrapingJobRepository>();
        services.AddScoped<IAiVisibilityRepository, AiVisibilityRepository>();
        services.AddScoped<IWebScraperService, WebScraperService>();
        services.AddScoped<IAiAnalysisService, DummyAiAnalysisService>();
        services.AddScoped<IMarkdownGeneratorService, MarkdownGeneratorService>();
        services.AddScoped<IScraperEngine, PlaywrightScraperEngine>();
        services.AddScoped<IScrapingJobService, ScrapingJobService>();
        services.AddScoped<IAiVisibilityEngineService, AiVisibilityEngineService>();

        services.AddHttpClient<ICmsIntegrationService, WordPressIntegrationService>();
        services.AddHttpClient<IOpenRouterService, OpenRouterService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddScoped<ISearchService, MockSearchService>();
        services.AddScoped<IMetricsCalculationService, MetricsCalculationService>();
        
        services.AddHostedService<Citationly.Infrastructure.BackgroundJobs.RecurringScrapeService>();
        return services;
    }
}
