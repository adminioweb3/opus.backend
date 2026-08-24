using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

/// <summary>
/// Real Anthropic Messages API call. IsConfigured is false (and this provider is simply skipped
/// by the registry - never faked) until Anthropic:ApiKey resolves to a real key. There is no
/// per-vendor persona-simulation fallback here or anywhere else in this class, by design.
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    private const decimal InputCostPerMillionTokens = 0.80m; // Claude Haiku-class pricing estimate - verify against Anthropic's current pricing page
    private const decimal OutputCostPerMillionTokens = 4.00m;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public AnthropicProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["Anthropic:ApiKey"]);
        _model = ConfigPlaceholderHelper.Resolve(configuration["Anthropic:Model"]) ?? "claude-3-5-haiku-latest";
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "Claude";
    public string ProviderKey => "anthropic";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => false;

    public async Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Anthropic is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:anthropic", cancellationToken);

        var body = new
        {
            model = _model,
            system = systemPrompt,
            max_tokens = 700,
            messages = new[] { new { role = "user", content = userPrompt } }
        };

        return await _aiResilience.ExecuteAsync("provider:anthropic", async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"Anthropic returned {(int)response.StatusCode}");
                }
                throw new InvalidOperationException($"Anthropic call failed: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;

            int? promptTokens = null, completionTokens = null;
            decimal? cost = null;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("input_tokens", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("output_tokens", out var ct2) ? ct2.GetInt32() : null;
                if (promptTokens.HasValue && completionTokens.HasValue)
                {
                    cost = (promptTokens.Value * InputCostPerMillionTokens + completionTokens.Value * OutputCostPerMillionTokens) / 1_000_000m;
                }
            }

            return new AiProviderResult(content, _model, promptTokens, completionTokens, cost, WasSearchGrounded: false);
        }, cancellationToken);
    }
}
