using Microsoft.Extensions.DependencyInjection;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Data;
using Citationly.Infrastructure.Repositories;
using Citationly.Infrastructure.Services;

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
        services.AddTransient<IMetricsRepository, MetricsRepository>();
        services.AddTransient<IWebScraperService, WebScraperService>();
        services.AddTransient<IAiAnalysisService, DummyAiAnalysisService>();

        services.AddHttpClient<ICmsIntegrationService, WordPressIntegrationService>();
        services.AddHttpClient<IOpenRouterService, OpenRouterService>();
        
        services.AddHostedService<Citationly.Infrastructure.BackgroundJobs.RecurringScrapeService>();
        return services;
    }
}
