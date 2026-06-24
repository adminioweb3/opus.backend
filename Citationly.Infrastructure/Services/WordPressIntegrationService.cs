using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class WordPressIntegrationService : ICmsIntegrationService
{
    private readonly ILogger<WordPressIntegrationService> _logger;
    private readonly HttpClient _httpClient;

    public string PlatformName => "WordPress";

    public WordPressIntegrationService(ILogger<WordPressIntegrationService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<bool> ValidateCredentialsAsync(string apiUrl, string apiKey)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl.TrimEnd('/')}/wp-json/wp/v2/users/me");
            
            // Assuming Application Password for basic auth
            // The apiKey should be formatted as "username:application_password"
            var authBytes = System.Text.Encoding.ASCII.GetBytes(apiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate WordPress credentials for URL: {ApiUrl}", apiUrl);
            return false;
        }
    }

    public async Task FetchAndStoreDataAsync(Guid organizationId, Integration integration)
    {
        // For MVP, just log. In the future, fetch posts/pages and map them to CrawledPages.
        _logger.LogInformation("Fetching data from WordPress for Organization {OrgId} via API {ApiUrl}", organizationId, integration.ApiUrl);
        await Task.CompletedTask;
    }

    public async Task<string> DeployContentAsync(Integration integration, string title, string content, string status)
    {
        try
        {
            var requestBody = new
            {
                title = title,
                content = content,
                status = status
            };

            var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"{integration.ApiUrl.TrimEnd('/')}/wp-json/wp/v2/posts")
            {
                Content = jsonContent
            };

            var authBytes = System.Text.Encoding.ASCII.GetBytes(integration.ApiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseString);
            var deployedUrl = jsonDoc.RootElement.GetProperty("link").GetString();

            return deployedUrl ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy content to WordPress for Integration {IntegrationId}", integration.Id);
            throw;
        }
    }
}
