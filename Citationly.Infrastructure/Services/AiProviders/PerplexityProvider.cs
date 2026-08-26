using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

/// <summary>
/// Real Perplexity API call (OpenAI-compatible chat completions surface). IsConfigured is false
/// (skipped by the registry, never faked) until Perplexity:ApiKey resolves to a real key.
/// Perplexity's "sonar" models are natively web-search-grounded, so this is the one provider
/// that can genuinely answer "what does live AI search actually show right now" - the exact
/// capability the audit found missing everywhere else.
/// </summary>
public sealed class PerplexityProvider : IAiProvider
{
    private const decimal InputCostPerMillionTokens = 1.00m; // Perplexity sonar pricing estimate - verify against Perplexity's current pricing page
    private const decimal OutputCostPerMillionTokens = 1.00m;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public PerplexityProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["Perplexity:ApiKey"]);
        _model = ConfigPlaceholderHelper.Resolve(configuration["Perplexity:Model"]) ?? "sonar";
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "Perplexity";
    public string ProviderKey => "perplexity";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => true;

    public async Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Perplexity is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:perplexity", cancellationToken);

        var body = new
        {
            model = _model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        return await _aiResilience.ExecuteAsync("provider:perplexity", async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.perplexity.ai/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"Perplexity returned {(int)response.StatusCode}");
                }
                throw new InvalidOperationException($"Perplexity call failed: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;

            int? promptTokens = null, completionTokens = null;
            decimal? cost = null;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var cpt) ? cpt.GetInt32() : null;
                if (promptTokens.HasValue && completionTokens.HasValue)
                {
                    cost = (promptTokens.Value * InputCostPerMillionTokens + completionTokens.Value * OutputCostPerMillionTokens) / 1_000_000m;
                }
            }

            await _aiUsageLimiter.RecordEstimatedCostAsync(_aiContext.OrganizationId, cost, "provider:perplexity", ct);
            return new AiProviderResult(content, _model, promptTokens, completionTokens, cost, WasSearchGrounded: true);
        }, cancellationToken);
    }
}
