using Citationly.Application.Interfaces;
using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Citationly.Tests;

public class OpenAiServiceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("${OPENAI_API_KEY}")]
    public async Task GenerateContentAsync_WithRequireJsonAndMissingKey_Throws(string? apiKey)
    {
        var configValues = new Dictionary<string, string?>();
        if (apiKey != null)
        {
            configValues["OpenAI:ApiKey"] = apiKey;
        }

        var service = new OpenAiService(
            new HttpClient(),
            new ConfigurationBuilder().AddInMemoryCollection(configValues).Build(),
            new StubAiRequestContextAccessor(),
            new StubAiUsageLimiter(),
            new StubAiResilienceService());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GenerateContentAsync("{}", requireJson: true));
    }

    private sealed class StubAiRequestContextAccessor : IAiRequestContextAccessor
    {
        public Guid? OrganizationId { get; set; }
    }

    private sealed class StubAiUsageLimiter : IAiUsageLimiter
    {
        public Task EnsureWithinLimitsAsync(Guid? organizationId, string operationName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RecordEstimatedCostAsync(Guid? organizationId, decimal? costUsd, string operationName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubAiResilienceService : IAiResilienceService
    {
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }
}
