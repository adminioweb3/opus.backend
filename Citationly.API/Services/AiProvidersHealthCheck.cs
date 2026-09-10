using Citationly.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Citationly.API.Services;

public sealed class AiProvidersHealthCheck : IHealthCheck
{
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IConfiguration _configuration;

    public AiProvidersHealthCheck(IAiProviderRegistry providerRegistry, IConfiguration configuration)
    {
        _providerRegistry = providerRegistry;
        _configuration = configuration;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_configuration.GetValue<bool>("AI:EmergencyDisable"))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "AI providers are emergency-disabled by configuration.",
                data: new Dictionary<string, object>
                {
                    ["configuredProviders"] = Array.Empty<string>(),
                    ["emergencyDisable"] = true
                }));
        }

        var configuredProviders = _providerRegistry
            .GetConfiguredProviders()
            .Select(provider => new
            {
                provider.ProviderKey,
                provider.PlatformName,
                provider.SupportsWebSearch
            })
            .ToArray();

        var data = new Dictionary<string, object>
        {
            ["configuredProviders"] = configuredProviders.Select(provider => provider.ProviderKey).ToArray(),
            ["providerCount"] = configuredProviders.Length,
            ["webSearchCapableProviders"] = configuredProviders
                .Where(provider => provider.SupportsWebSearch)
                .Select(provider => provider.ProviderKey)
                .ToArray(),
            ["emergencyDisable"] = false
        };

        return configuredProviders.Length == 0
            ? Task.FromResult(HealthCheckResult.Degraded("No real AI provider credentials are configured.", data: data))
            : Task.FromResult(HealthCheckResult.Healthy("At least one real AI provider is configured.", data));
    }
}
