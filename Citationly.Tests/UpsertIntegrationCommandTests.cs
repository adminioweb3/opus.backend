using Citationly.Application.Features.Integrations;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Xunit;

namespace Citationly.Tests;

public class UpsertIntegrationCommandTests
{
    [Theory]
    [InlineData("${WORDPRESS_URL}", "real-user:real-application-password")]
    [InlineData("https://site.example/wp-json", "${WORDPRESS_API_KEY}")]
    [InlineData("https://site.example/wp-json", "your-api-key")]
    [InlineData("https://site.example/wp-json", "demo")]
    public async Task Handle_RejectsPlaceholderIntegrationConfig_BeforeCallingCms(string apiUrl, string apiKey)
    {
        var repository = new StubIntegrationRepository();
        var cms = new StubCmsIntegrationService();
        var handler = new UpsertIntegrationCommandHandler(repository, new[] { cms });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new UpsertIntegrationCommand
        {
            OrganizationId = Guid.NewGuid(),
            PlatformName = "WordPress",
            ApiUrl = apiUrl,
            ApiKey = apiKey
        }, CancellationToken.None));

        Assert.Contains("real values", ex.Message);
        Assert.Equal(0, cms.ValidateCalls);
        Assert.Equal(0, repository.UpsertCalls);
    }

    [Fact]
    public async Task Handle_SavesIntegration_WhenCredentialsValidate()
    {
        var repository = new StubIntegrationRepository();
        var cms = new StubCmsIntegrationService { ValidationResult = true };
        var handler = new UpsertIntegrationCommandHandler(repository, new[] { cms });

        var id = await handler.Handle(new UpsertIntegrationCommand
        {
            OrganizationId = Guid.NewGuid(),
            PlatformName = "WordPress",
            ApiUrl = "https://cms.customer.test/wp-json",
            ApiKey = "editor:real-application-password"
        }, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(1, cms.ValidateCalls);
        Assert.Equal(1, repository.UpsertCalls);
    }

    private sealed class StubCmsIntegrationService : ICmsIntegrationService
    {
        public string PlatformName => "WordPress";
        public bool ValidationResult { get; init; }
        public int ValidateCalls { get; private set; }

        public Task<bool> ValidateCredentialsAsync(string apiUrl, string apiKey)
        {
            ValidateCalls++;
            return Task.FromResult(ValidationResult);
        }

        public Task FetchAndStoreDataAsync(Guid organizationId, Integration integration) => Task.CompletedTask;
        public Task<string> DeployContentAsync(Integration integration, string title, string content, string status) => Task.FromResult("https://published.example/post");
    }

    private sealed class StubIntegrationRepository : IIntegrationRepository
    {
        public int UpsertCalls { get; private set; }

        public Task<IEnumerable<Integration>> GetIntegrationsByOrgAsync(Guid organizationId) => Task.FromResult(Enumerable.Empty<Integration>());
        public Task<Integration?> GetIntegrationByOrgAndPlatformAsync(Guid organizationId, string platformName) => Task.FromResult<Integration?>(null);
        public Task<Integration?> GetIntegrationByIdAsync(Guid id, Guid organizationId) => Task.FromResult<Integration?>(null);
        public Task<Guid> UpsertIntegrationAsync(Integration integration)
        {
            UpsertCalls++;
            return Task.FromResult(Guid.NewGuid());
        }
    }
}
