using Citationly.Application.Interfaces;
using Citationly.Application.Services;
using Xunit;

namespace Citationly.Tests;

public class AiCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsUnavailable_WhenNoProvidersAreConfigured()
    {
        var service = new AiCompletionService(new StubProviderRegistry(), new StubAiRequestContextAccessor(), new InMemoryAiCompletionCache());

        var result = await service.CompleteAsync(Guid.NewGuid(), "test.operation", "user", "system");

        Assert.False(result.Success);
        Assert.Contains("No AI providers are configured", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_ReturnsFailure_WhenJsonIsRequiredAndProviderReturnsText()
    {
        var context = new StubAiRequestContextAccessor();
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("not json", "test-model", 1, 2, 0.01m, false) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context, new InMemoryAiCompletionCache());

        var result = await service.CompleteAsync(Guid.NewGuid(), "json.operation", "user", "system", requireJson: true);

        Assert.False(result.Success);
        Assert.Equal("stub", result.ProviderKey);
        Assert.Contains("invalid JSON", result.ErrorMessage);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotCacheInvalidJson_ForJsonRequiredOperations()
    {
        var context = new StubAiRequestContextAccessor();
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("not json", "test-model", 1, 2, 0.01m, false) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context, new InMemoryAiCompletionCache());
        var orgId = Guid.NewGuid();

        await service.CompleteAsync(orgId, "json.operation", "same user", "same system", requireJson: true);
        provider.Result = new AiProviderResult("{\"ok\":true}", "test-model", 1, 2, 0.01m, false);
        var second = await service.CompleteAsync(orgId, "json.operation", "same user", "same system", requireJson: true);

        Assert.True(second.Success);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_ForwardsJsonRequirement_ToProvider()
    {
        var context = new StubAiRequestContextAccessor();
        var provider = new StubAiProvider(context)
        {
            Result = new AiProviderResult("{\"ok\":true}", "test-model", 1, 2, 0.01m, false)
        };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context, new InMemoryAiCompletionCache());

        var result = await service.CompleteAsync(
            Guid.NewGuid(),
            "json.operation",
            "user",
            "system",
            requireJson: true);

        Assert.True(result.Success);
        Assert.True(provider.ObservedRequireJson);
    }

    [Fact]
    public async Task CompleteAsync_SetsAndRestoresOrganizationContext()
    {
        var originalOrgId = Guid.NewGuid();
        var callOrgId = Guid.NewGuid();
        var context = new StubAiRequestContextAccessor { OrganizationId = originalOrgId };
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("{\"ok\":true}", "test-model", 1, 2, 0.01m, false) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context, new InMemoryAiCompletionCache());

        var result = await service.CompleteAsync(callOrgId, "json.operation", "user", "system", requireJson: true);

        Assert.True(result.Success);
        Assert.Equal(callOrgId, provider.ObservedOrganizationId);
        Assert.Equal(originalOrgId, context.OrganizationId);
    }

    [Fact]
    public async Task CompleteAsync_ReusesFreshCachedCompletion_ForSamePromptAndProvider()
    {
        var context = new StubAiRequestContextAccessor();
        var provider = new StubAiProvider(context) { Result = new AiProviderResult("{\"ok\":true}", "test-model", 11, 22, 0.03m, true) };
        var service = new AiCompletionService(new StubProviderRegistry(provider), context, new InMemoryAiCompletionCache());
        var orgId = Guid.NewGuid();

        var first = await service.CompleteAsync(orgId, "cache.operation", "same user", "same system", requireJson: true);
        var second = await service.CompleteAsync(orgId, "cache.operation", "same user", "same system", requireJson: true);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(first.Content, second.Content);
        Assert.Null(second.CostUsd);
        Assert.Null(second.PromptTokens);
        Assert.Null(second.CompletionTokens);
    }

    [Fact]
    public async Task CompleteAsync_HonorsPreferredOpenAiProvider()
    {
        var context = new StubAiRequestContextAccessor();
        var directOpenAi = new StubAiProvider(context) { ProviderKey = "openai" };
        var otherProvider = new StubAiProvider(context) { ProviderKey = "other" };
        var service = new AiCompletionService(
            new StubProviderRegistry(otherProvider, directOpenAi),
            context,
            new InMemoryAiCompletionCache());

        var result = await service.CompleteAsync(
            Guid.NewGuid(),
            "analysis.operation",
            "user",
            "system",
            preferredProviderKey: "openai");

        Assert.True(result.Success);
        Assert.Equal("openai", result.ProviderKey);
        Assert.Equal(1, directOpenAi.CallCount);
        Assert.Equal(0, otherProvider.CallCount);
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

        public AiProviderResult Result { get; set; } = new("{\"ok\":true}", "test-model", null, null, null, false);
        public Guid? ObservedOrganizationId { get; private set; }
        public int CallCount { get; private set; }
        public bool ObservedRequireJson { get; private set; }
        public string PlatformName => "Stub";
        public string ProviderKey { get; init; } = "stub";
        public bool IsConfigured => true;
        public bool SupportsWebSearch => false;

        public Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            CallCount++;
            ObservedOrganizationId = _context.OrganizationId;
            return Task.FromResult(Result);
        }

        public Task<AiProviderResult> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            bool requireJson,
            CancellationToken cancellationToken = default)
        {
            ObservedRequireJson = requireJson;
            return CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        }
    }
}
