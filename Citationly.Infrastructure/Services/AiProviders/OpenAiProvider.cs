using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

/// <summary>
/// The one provider that was already real (LLMRunnerService's own comment called this the
/// "unmodified GPT-4o-mini answer" case) - kept as-is behaviorally, just moved behind IAiProvider
/// and made to capture usage/cost, which the old direct-HttpClient code discarded.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    // Per-token USD rates for gpt-4o-mini, as published on OpenAI's pricing page at the time
    // this was written. Update these constants if the model or its pricing changes - this cost
    // is an estimate for internal budget tracking, not an invoice-grade figure.
    private const decimal InputCostPerMillionTokens = 0.15m;
    private const decimal OutputCostPerMillionTokens = 0.60m;
    private const string Model = "gpt-4o-mini";

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public OpenAiProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:ApiKey"]);
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "ChatGPT";
    public string ProviderKey => "openai";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => false;

    public async Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("OpenAI is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:openai", cancellationToken);

        var body = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 700
        };

        return await _aiResilience.ExecuteAsync("provider:openai", async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode}");
                }
                throw new InvalidOperationException($"OpenAI call failed: {response.StatusCode} - {responseText}");
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

            return new AiProviderResult(content, Model, promptTokens, completionTokens, cost, WasSearchGrounded: false);
        }, cancellationToken);
    }
}
