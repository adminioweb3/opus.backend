using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

/// <summary>
/// Real Google Gemini API call. IsConfigured is false (skipped by the registry, never faked)
/// until Google:ApiKey resolves to a real key. Search grounding (tools: [{google_search: {}}])
/// is left disabled by default (SupportsWebSearch = false) since it can't be verified without a
/// live key - enable Google:EnableSearchGrounding once a real key is in place and the response
/// shape has been confirmed against Google's current API docs.
/// </summary>
public sealed class GoogleGeminiProvider : IAiProvider
{
    private const decimal InputCostPerMillionTokens = 0.075m; // Gemini Flash-class pricing estimate - verify against Google's current pricing page
    private const decimal OutputCostPerMillionTokens = 0.30m;

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly bool _enableSearchGrounding;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public GoogleGeminiProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["Google:ApiKey"]);
        _model = ConfigPlaceholderHelper.Resolve(configuration["Google:Model"]) ?? "gemini-2.0-flash";
        _enableSearchGrounding = configuration.GetValue("Google:EnableSearchGrounding", false);
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "Gemini";
    public string ProviderKey => "google";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => _enableSearchGrounding;

    public async Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Google Gemini is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:google", cancellationToken);

        object body = _enableSearchGrounding
            ? new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } },
                tools = new object[] { new { google_search = new { } } }
            }
            : new
            {
                systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
                contents = new[] { new { role = "user", parts = new[] { new { text = userPrompt } } } }
            };

        return await _aiResilience.ExecuteAsync("provider:google", async ct =>
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"Google Gemini returned {(int)response.StatusCode}");
                }
                throw new InvalidOperationException($"Google Gemini call failed: {response.StatusCode} - {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            var content = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            int? promptTokens = null, completionTokens = null;
            decimal? cost = null;
            if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
            {
                promptTokens = usage.TryGetProperty("promptTokenCount", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("candidatesTokenCount", out var cpt) ? cpt.GetInt32() : null;
                if (promptTokens.HasValue && completionTokens.HasValue)
                {
                    cost = (promptTokens.Value * InputCostPerMillionTokens + completionTokens.Value * OutputCostPerMillionTokens) / 1_000_000m;
                }
            }

            await _aiUsageLimiter.RecordEstimatedCostAsync(_aiContext.OrganizationId, cost, "provider:google", ct);
            return new AiProviderResult(content, _model, promptTokens, completionTokens, cost, WasSearchGrounded: _enableSearchGrounding);
        }, cancellationToken);
    }
}
