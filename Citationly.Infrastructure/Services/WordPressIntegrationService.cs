using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Security;
using Citationly.Domain.Entities;

namespace Citationly.Infrastructure.Services;

public class WordPressIntegrationService : ICmsIntegrationService
{
    private readonly ILogger<WordPressIntegrationService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IOutboundUrlSafetyValidator _urlSafetyValidator;

    public string PlatformName => "WordPress";

    public WordPressIntegrationService(
        ILogger<WordPressIntegrationService> logger,
        HttpClient httpClient,
        IOutboundUrlSafetyValidator urlSafetyValidator)
    {
        _logger = logger;
        _httpClient = httpClient;
        _urlSafetyValidator = urlSafetyValidator;
    }

    public async Task<bool> ValidateCredentialsAsync(string apiUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        try
        {
            var endpoint = await BuildWordPressEndpointAsync(apiUrl, "/wp-json/wp/v2/users/me");
            if (endpoint == null) return false;

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            
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
        if (string.IsNullOrWhiteSpace(integration.ApiUrl) || string.IsNullOrWhiteSpace(integration.ApiKey))
        {
            throw new InvalidOperationException("WordPress integration is missing its API URL or API key.");
        }

        try
        {
            var requestBody = new
            {
                title = title,
                content = content,
                status = status
            };

            var jsonContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
            var endpoint = await BuildWordPressEndpointAsync(integration.ApiUrl, "/wp-json/wp/v2/posts");
            if (endpoint == null)
            {
                throw new InvalidOperationException("WordPress API URL is not allowed.");
            }
            
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
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

    private async Task<string?> BuildWordPressEndpointAsync(string apiUrl, string path)
    {
        var safety = await _urlSafetyValidator.ValidateForHttpFetchAsync(apiUrl, allowMissingScheme: true);
        if (!safety.IsAllowed || string.IsNullOrWhiteSpace(safety.NormalizedUrl))
        {
            _logger.LogWarning("Blocked unsafe WordPress API URL: {Reason}", safety.Reason);
            return null;
        }

        return new Uri(new Uri(safety.NormalizedUrl), path).ToString();
    }
}
