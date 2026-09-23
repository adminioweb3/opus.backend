using System.Net;
using System.Text;
using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services.AiProviders;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class OpenAiProviderTests
{
    [Fact]
    public async Task CompleteAsync_RequestsJsonObject_WhenJsonIsRequired()
    {
        var handler = new RecordingHandler("""
            {
              "choices":[{"message":{"content":"{\"ok\":true}"}}],
              "usage":{"prompt_tokens":1,"completion_tokens":2}
            }
            """);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-key",
            ["OpenAI:EnableWebSearch"] = "false"
        }).Build();
        var provider = new OpenAiProvider(
            new HttpClient(handler),
            configuration,
            new StubRequestContext(),
            new NoOpUsageLimiter(),
            new PassthroughResilience());

        var result = await provider.CompleteAsync("system", "user", requireJson: true);

        Assert.Equal("{\"ok\":true}", result.Content);
        Assert.Contains("\"response_format\":{\"type\":\"json_object\"}", handler.RequestBody);
    }

    [Fact]
    public async Task CompleteWithWebSearch_UsesSearchModel_AndCapturesCitations()
    {
        var handler = new RecordingHandler("""
            {
              "choices":[{"message":{"content":"Observed answer","annotations":[
                {"type":"url_citation","url_citation":{"url":"https://example.com/source","title":"Source"}}
              ]}}],
              "usage":{"prompt_tokens":3,"completion_tokens":4}
            }
            """);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenAI:ApiKey"] = "test-key",
            ["OpenAI:EnableWebSearch"] = "true",
            ["OpenAI:SearchModel"] = "gpt-5-search-api"
        }).Build();
        var provider = new OpenAiProvider(
            new HttpClient(handler),
            configuration,
            new StubRequestContext(),
            new NoOpUsageLimiter(),
            new PassthroughResilience());

        var result = await provider.CompleteWithWebSearchAsync("system", "user");

        Assert.True(result.WasSearchGrounded);
        Assert.Equal("gpt-5-search-api", result.ModelUsed);
        Assert.Contains("https://example.com/source", result.Citations!);
        Assert.Contains("\"model\":\"gpt-5-search-api\"", handler.RequestBody);
        Assert.Contains("\"max_completion_tokens\"", handler.RequestBody);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StubRequestContext : IAiRequestContextAccessor
    {
        public Guid? OrganizationId { get; set; } = Guid.NewGuid();
    }

    private sealed class NoOpUsageLimiter : IAiUsageLimiter
    {
        public Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class PassthroughResilience : IAiResilienceService
    {
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }
}
