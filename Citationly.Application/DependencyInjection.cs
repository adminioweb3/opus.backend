using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Citationly.Application.Features.Assistant.Pipeline;
using Citationly.Application.Features.Assistant.Services;

namespace Citationly.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
        
        // Assistant Pipeline Services
        services.AddScoped<OpenAiClientService>();
        services.AddScoped<IntentDetectionService>();
        services.AddScoped<ToolExecutionService>();
        services.AddScoped<ContextBuilderService>();
        services.AddScoped<AnalyticsEngineService>();
        services.AddScoped<PromptBuilderService>();
        services.AddScoped<AgentOrchestrator>();
        
        // Prompt Intelligence Services
        services.AddScoped<Citationly.Application.Features.PromptIntelligence.Services.ILLMRunnerService, Citationly.Application.Features.PromptIntelligence.Services.LLMRunnerService>();
        services.AddScoped<Citationly.Application.Features.PromptIntelligence.Services.IVisibilityCalculatorService, Citationly.Application.Features.PromptIntelligence.Services.VisibilityCalculatorService>();
        services.AddScoped<Citationly.Application.Features.PromptIntelligence.Services.IRecommendationEngineService, Citationly.Application.Features.PromptIntelligence.Services.RecommendationEngineService>();
        services.AddScoped<Citationly.Application.Features.PromptIntelligence.Services.IPromptExecutionService, Citationly.Application.Features.PromptIntelligence.Services.PromptExecutionService>();

        // GEO Dashboard
        services.AddScoped<Citationly.Application.Features.GeoDashboard.GeoDashboardAggregator>();
        services.AddScoped<Citationly.Application.Interfaces.GeoDashboard.IGeoPillarService, Citationly.Application.Features.GeoDashboard.GeoPillarService>();
        services.AddScoped<Citationly.Application.Interfaces.GeoDashboard.IPromptCoverageService, Citationly.Application.Features.GeoDashboard.PromptCoverageService>();
        services.AddScoped<Citationly.Application.Interfaces.GeoDashboard.IActivityFeedService, Citationly.Application.Features.GeoDashboard.ActivityFeedService>();
        services.AddScoped<Citationly.Application.Interfaces.GeoDashboard.IEngineScanService, Citationly.Application.Features.GeoDashboard.EngineScanService>();

        // Command Center
        services.AddScoped<Citationly.Application.Features.CommandCenter.CommandCenterAggregator>();

        // Opportunity Finder
        services.AddScoped<Citationly.Application.Features.Opportunities.OpportunityFinderAggregator>();

        return services;
    }
}
