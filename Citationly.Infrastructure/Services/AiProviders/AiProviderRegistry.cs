using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services.AiProviders;

public sealed class AiProviderRegistry : IAiProviderRegistry
{
    private readonly IReadOnlyList<IAiProvider> _allProviders;

    public AiProviderRegistry(IEnumerable<IAiProvider> providers)
    {
        _allProviders = providers.ToList();
    }

    public IReadOnlyList<IAiProvider> GetAllProviders() => _allProviders;

    public IReadOnlyList<IAiProvider> GetConfiguredProviders() =>
        _allProviders.Where(p => p.IsConfigured).ToList();
}
