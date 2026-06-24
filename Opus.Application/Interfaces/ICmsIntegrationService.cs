using Opus.Domain.Entities;

namespace Opus.Application.Interfaces;

public interface ICmsIntegrationService
{
    string PlatformName { get; }
    Task<bool> ValidateCredentialsAsync(string apiUrl, string apiKey);
    Task FetchAndStoreDataAsync(Guid organizationId, Integration integration);
    Task<string> DeployContentAsync(Integration integration, string title, string content, string status);
}
