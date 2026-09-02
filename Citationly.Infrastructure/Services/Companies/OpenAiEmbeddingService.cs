using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Application.Interfaces.Companies;
using Citationly.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.Companies;

public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public string ModelName => "text-embedding-3-small";

    public OpenAiEmbeddingService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:ApiKey"]) ?? string.Empty;
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public async Task<double[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrWhiteSpace(text)) return null;

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "openai.embedding", cancellationToken);

        // text-embedding-3-small's context window is 8191 tokens — a generous char cap keeps
        // callers from having to think about tokenization themselves.
        var input = text.Length > 24000 ? text[..24000] : text;

        try
        {
            return await _aiResilience.ExecuteAsync("openai.embedding", async ct =>
            {
                var requestBody = new { model = ModelName, input };
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, ct);
                var responseString = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        throw new HttpRequestException($"OpenAI embeddings failed with {response.StatusCode}");
                    }

                    throw new InvalidOperationException($"OpenAI embeddings failed with {response.StatusCode}: {responseString}");
                }

                using var doc = JsonDocument.Parse(responseString);
                var vectorElement = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
                return vectorElement.EnumerateArray().Select(e => e.GetDouble()).ToArray();
            }, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
