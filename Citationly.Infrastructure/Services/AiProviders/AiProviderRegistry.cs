using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly IReadOnlyList<IAiProvider> _allProviders;
    private readonly IConfiguration _configuration;

    public AiProviderRegistry(IEnumerable<IAiProvider> providers, IConfiguration configuration)
    {
        _allProviders = providers.ToList();
        _configuration = configuration;
    }

    public IReadOnlyList<IAiProvider> GetAllProviders() => _allProviders;

    public IReadOnlyList<IAiProvider> GetConfiguredProviders()
    {
        if (_configuration.GetValue<bool>("AI:EmergencyDisable")) return Array.Empty<IAiProvider>();
        return _allProviders.Where(p => p.IsConfigured).ToList();
    }
}
