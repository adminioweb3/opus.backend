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

    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string _searchModel;
    private readonly bool _enableWebSearch;
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
        _model = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:Model"]) ?? "gpt-4o-mini";
        _searchModel = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:SearchModel"]) ?? "gpt-4o-mini-search-preview";
        _enableWebSearch = configuration.GetValue("OpenAI:EnableWebSearch", true);
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "ChatGPT";
    public string ProviderKey => "openai";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => _enableWebSearch;

    public async Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("OpenAI is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:openai", cancellationToken);

        if (_enableWebSearch)
        {
            return await CompleteWithResponsesWebSearchAsync(systemPrompt, userPrompt, cancellationToken);
        }

        var body = new
        {
            model = _model,
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

            await _aiUsageLimiter.RecordEstimatedCostAsync(_aiContext.OrganizationId, cost, "provider:openai", ct);
            return new AiProviderResult(content, _model, promptTokens, completionTokens, cost, WasSearchGrounded: false);
        }, cancellationToken);
    }

    private async Task<AiProviderResult> CompleteWithResponsesWebSearchAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var body = new
        {
            model = _searchModel,
            instructions = systemPrompt,
            input = userPrompt,
            tools = new[] { new { type = "web_search_preview" } },
            max_output_tokens = 700
        };

        return await _aiResilience.ExecuteAsync("provider:openai", async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
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
            var content = ExtractResponsesOutputText(doc.RootElement);

            int? promptTokens = null, completionTokens = null;
            decimal? cost = null;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("input_tokens", out var inputTokens) ? inputTokens.GetInt32() : null;
                completionTokens = usage.TryGetProperty("output_tokens", out var outputTokens) ? outputTokens.GetInt32() : null;
                if (promptTokens.HasValue && completionTokens.HasValue)
                {
                    cost = (promptTokens.Value * InputCostPerMillionTokens + completionTokens.Value * OutputCostPerMillionTokens) / 1_000_000m;
                }
            }

            await _aiUsageLimiter.RecordEstimatedCostAsync(_aiContext.OrganizationId, cost, "provider:openai", ct);
            return new AiProviderResult(content, _searchModel, promptTokens, completionTokens, cost, WasSearchGrounded: true);
        }, cancellationToken);
    }

    private static string ExtractResponsesOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "message") continue;
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }
        }

        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
