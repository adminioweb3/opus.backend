using Citationly.Application.Features.PromptIntelligence.Services;
using Citationly.Application.Interfaces;
using Citationly.Application.Services;
using Xunit;

namespace Citationly.Tests;

public class LLMRunnerServiceTests
{
    [Fact]
    public async Task RunPromptAcrossModels_CapturesThreeIndependentSamplesPerProvider()
    {
        var provider = new ProviderStub();
        var runner = new LLMRunnerService(new RegistryStub(provider), new InMemoryAiCompletionCache());

        var responses = (await runner.RunPromptAcrossModelsAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Which product should I choose?",
            CancellationToken.None)).ToList();

        Assert.Equal(LLMRunnerService.SamplesPerProvider, responses.Count);
        Assert.Equal(LLMRunnerService.SamplesPerProvider, provider.CallCount);
        Assert.Equal(responses.Count, responses.Select(response => response.Id).Distinct().Count());
        Assert.All(responses, response =>
        {
            Assert.Equal("openai", response.ProviderKey);
            Assert.Equal("prompt-intelligence:v3-search-grounded-sampled", response.PromptVersion);
        });

        await runner.RunPromptAcrossModelsAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Which product should I choose?",
            CancellationToken.None);

        Assert.Equal(LLMRunnerService.SamplesPerProvider * 2, provider.CallCount);
    }

    private sealed class RegistryStub : IAiProviderRegistry
    {
        private readonly IReadOnlyList<IAiProvider> _providers;

        public RegistryStub(params IAiProvider[] providers) => _providers = providers;
        public IReadOnlyList<IAiProvider> GetConfiguredProviders() => _providers;
        public IReadOnlyList<IAiProvider> GetAllProviders() => _providers;
    }

    private sealed class ProviderStub : IAiProvider
    {
        public int CallCount { get; private set; }
        public string PlatformName => "ChatGPT";
        public string ProviderKey => "openai";
        public bool IsConfigured => true;
        public bool SupportsWebSearch => false;

        public Task<AiProviderResult> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new AiProviderResult(
                $"Sample {CallCount}",
                "test-openai-model",
                10,
                10,
                0.001m,
                false));
        }
    }
}
