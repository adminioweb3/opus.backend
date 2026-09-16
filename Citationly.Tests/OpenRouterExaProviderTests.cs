using System.Net;
using System.Text;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services.AiProviders;
using Citationly.Infrastructure.Services.WebEvidence;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class OpenRouterExaProviderTests
{
    [Fact]
    public void OpenRouter_IsNotConfigured_ForPlaceholderKey()
    {
        var provider = CreateOpenRouter(
            new Dictionary<string, string?> { ["OpenRouter:ApiKey"] = "${OPENROUTER_API_KEY}" },
            new StubHandler("{}"));

        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task OpenRouter_PinsModel_DisablesFallbacks_AndUsesReportedCost()
    {
        var handler = new StubHandler("""
            {
              "id":"gen-123",
              "model":"openai/gpt-test",
              "provider":"OpenAI",
              "choices":[{"message":{"content":"Measured answer"}}],
              "usage":{"prompt_tokens":12,"completion_tokens":34,"cost":0.0042}
            }
            """);
        var usage = new RecordingUsageLimiter();
        var provider = CreateOpenRouter(
            new Dictionary<string, string?>
            {
                ["OpenRouter:ApiKey"] = "test-key",
                ["OpenRouter:RequireZdr"] = "true",
                ["OpenRouter:DataCollection"] = "deny"
            },
            handler,
            usage);

        var result = await provider.CompleteAsync("system", "user");

        Assert.Equal("Measured answer", result.Content);
        Assert.Equal("openai/gpt-test", result.ModelUsed);
        Assert.Equal("openrouter", result.Gateway);
        Assert.Equal("OpenAI", result.UpstreamProvider);
        Assert.Equal("gen-123", result.GenerationId);
        Assert.Equal(0.0042m, result.CostUsd);
        Assert.Contains("\"allow_fallbacks\":false", handler.RequestBody);
        Assert.Contains("\"data_collection\":\"deny\"", handler.RequestBody);
        Assert.Contains("\"zdr\":true", handler.RequestBody);
        Assert.Equal(0.02m, usage.RecordedCost);
    }

    [Fact]
    public async Task Exa_ReturnsOnlyPublicDeduplicatedEvidence_AndReportedCost()
    {
        var handler = new StubHandler("""
            {
              "requestId":"exa-123",
              "costDollars":{"total":0.012},
              "results":[
                {"url":"https://example.com/guide","title":"Guide","publishedDate":"2026-01-01","author":"A","highlights":["Useful"],"highlightScores":[0.91]},
                {"url":"https://example.com/guide","title":"Duplicate","highlights":[],"highlightScores":[]},
                {"url":"javascript:alert(1)","title":"Unsafe","highlights":[],"highlightScores":[]}
              ]
            }
            """);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Exa:ApiKey"] = "test-key",
            ["Exa:Enabled"] = "true",
            ["Exa:SearchType"] = "fast"
        }).Build();
        var usage = new RecordingUsageLimiter();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.exa.ai/") };
        var provider = new ExaEvidenceProvider(client, config, usage, new PassthroughResilience());

        var result = await provider.SearchAsync(Guid.NewGuid(), new WebEvidenceQuery($"unique-{Guid.NewGuid():N}", 5));

        Assert.True(result.Success);
        Assert.Equal("exa-123", result.RequestId);
        Assert.Single(result.Items);
        Assert.Equal("https://example.com/guide", result.Items[0].Url);
        Assert.Equal(0.012m, result.CostUsd);
        Assert.Equal(0.012m, usage.RecordedCost);
    }

    private static OpenRouterProvider CreateOpenRouter(
        Dictionary<string, string?> values,
        StubHandler handler,
        RecordingUsageLimiter? usage = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new OpenRouterProvider(
            new HttpClient(handler),
            config,
            new OpenRouterModelDefinition("test", "OpenAI API", "openai/gpt-test", true, 800),
            new StubRequestContext(),
            usage ?? new RecordingUsageLimiter(),
            new PassthroughResilience());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string RequestBody { get; private set; } = string.Empty;

        public StubHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubRequestContext : IAiRequestContextAccessor
    {
        public Guid? OrganizationId { get; set; } = Guid.NewGuid();
    }

    private sealed class RecordingUsageLimiter : IAiUsageLimiter
    {
        public decimal? RecordedCost { get; private set; }
        public Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default)
        {
            RecordedCost = costUsd;
            return Task.CompletedTask;
        }
    }

    private sealed class PassthroughResilience : IAiResilienceService
    {
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }
}
