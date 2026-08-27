using Citationly.Application.Interfaces;
using Citationly.Application.Services;
using Xunit;

namespace Citationly.Tests;

public class AiCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsUnavailable_WhenNoProvidersAreConfigured()
    {
        var service = new AiCompletionService(new StubProviderRegistry(), new StubAiRequestContextAccessor());

        var result = await service.CompleteAsync(Guid.NewGuid(), "test.operation", "user", "system");

        Assert.False(result.Success);
        Assert.Contains("No AI providers are configured", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsFailure_WhenJsonIsRequiredAndProviderReturnsText()
    {
        var context = new StubAiRequestContextAccessor();
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("not json", "test-model", 1, 2, 0.01m, false) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context);

        var result = await service.CompleteAsync(Guid.NewGuid(), "json.operation", "user", "system", requireJson: true);

        Assert.False(result.Success);
        Assert.Equal("stub", result.ProviderKey);
        Assert.Contains("invalid JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_SetsAndRestoresOrganizationContext()
    {
        var originalOrgId = Guid.NewGuid();
        var callOrgId = Guid.NewGuid();
        var context = new StubAiRequestContextAccessor { OrganizationId = originalOrgId };
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("{\"ok\":true}", "test-model", 1, 2, 0.01m, false) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context);

        var result = await service.CompleteAsync(callOrgId, "json.operation", "user", "system", requireJson: true);

        Assert.True(result.Success);
        Assert.Equal(callOrgId, provider.ObservedOrganizationId);
        Assert.Equal(originalOrgId, context.OrganizationId);
    }

    private sealed class StubProviderRegistry : IAiProviderRegistry
    {
        private readonly IReadOnlyList<IAiProvider> _providers;

        public StubProviderRegistry(params IAiProvider[] providers)
        {
            _providers = providers;
        }

        public IReadOnlyList<IAiProvider> GetConfiguredProviders() => _providers.Where(p => p.IsConfigured).ToList();

        public IReadOnlyList<IAiProvider> GetAllProviders() => _providers;
    }

    private sealed class StubAiRequestContextAccessor : IAiRequestContextAccessor
    {
        public Guid? OrganizationId { get; set; }
    }

    private sealed class StubAiProvider : IAiProvider
    {
        private readonly StubAiRequestContextAccessor _context;

        public StubAiProvider(StubAiRequestContextAccessor context)
        {
            _context = context;
        }

        public AiProviderResult Result { get; init; } = new("{\"ok\":true}", "test-model", null, null, null, false);
        public Guid? ObservedOrganizationId { get; private set; }
        public string PlatformName => "Stub";
        public string ProviderKey => "stub";
        public bool IsConfigured => true;
        public bool SupportsWebSearch => false;

        public Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            ObservedOrganizationId = _context.OrganizationId;
            return Task.FromResult(Result);
        }
    }
}
