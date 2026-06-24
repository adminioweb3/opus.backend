using Microsoft.Extensions.DependencyInjection;
using Opus.Application.Interfaces;
using Opus.Infrastructure.Data;
using Opus.Infrastructure.Repositories;
using Opus.Infrastructure.Services;

namespace Opus.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IWebsiteRepository, WebsiteRepository>();
        services.AddTransient<IIntegrationRepository, IntegrationRepository>();
        services.AddTransient<IEmbeddingRepository, EmbeddingRepository>();
        services.AddTransient<IMetricsRepository, MetricsRepository>();
        services.AddTransient<IWebScraperService, WebScraperService>();
        services.AddTransient<IAiAnalysisService, DummyAiAnalysisService>();

        services.AddHttpClient<ICmsIntegrationService, WordPressIntegrationService>();
        services.AddHttpClient<IOpenRouterService, OpenRouterService>();
        
        services.AddHostedService<Opus.Infrastructure.BackgroundJobs.RecurringScrapeService>();
        return services;
    }
}
