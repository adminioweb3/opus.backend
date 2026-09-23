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
    private readonly int _maxTokens;
    private readonly int _searchMaxTokens;
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
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:ApiKey"], "OPENAI_API_KEY");
        _model = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:Model"]) ?? "gpt-4o-mini";
        _searchModel = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:SearchModel"]) ?? "gpt-5-search-api";
        _maxTokens = configuration.GetValue("OpenAI:MaxTokens", 4096);
        _searchMaxTokens = configuration.GetValue("OpenAI:SearchMaxTokens", _maxTokens);
        _enableWebSearch = configuration.GetValue("OpenAI:EnableWebSearch", true);
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => "OpenAI API";
    public string ProviderKey => "openai";
    public bool IsConfigured => _apiKey is not null;
    public bool SupportsWebSearch => _enableWebSearch;

    public Task<AiProviderResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(systemPrompt, userPrompt, requireJson: false, cancellationToken);

    public async Task<AiProviderResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        bool requireJson,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("OpenAI is not configured.");

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "provider:openai", cancellationToken);

        var body = CreateChatCompletionBody(_model, systemPrompt, userPrompt, _maxTokens, requireJson);

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

    public Task<AiProviderResult> CompleteWithWebSearchAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default) =>
        _enableWebSearch
            ? CompleteWithSearchModelAsync(systemPrompt, userPrompt, cancellationToken)
            : CompleteAsync(systemPrompt, userPrompt, cancellationToken);

    private async Task<AiProviderResult> CompleteWithSearchModelAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        // OpenAI's Chat Completions search model always retrieves from the web. Keep this path
        // exclusive to observation runs; JSON extraction and other internal synthesis calls use
        // the normal model and must not silently incur search calls.
        var body = CreateChatCompletionBody(_searchModel, systemPrompt, userPrompt, _searchMaxTokens, requireJson: false);

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
            var content = ExtractChatCompletionsOutputText(doc.RootElement);

            int? promptTokens = null, completionTokens = null;
            // Search-model pricing includes tool-call charges that cannot be reconstructed from
            // token counts alone. Leave cost null instead of recording an understated fake cost.
            decimal? cost = null;
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var cpt) ? cpt.GetInt32() : null;
            }

            await _aiUsageLimiter.RecordEstimatedCostAsync(_aiContext.OrganizationId, cost, "provider:openai", ct);
            var citations = ExtractChatCompletionCitationUrls(doc.RootElement);
            return new AiProviderResult(
                content,
                _searchModel,
                promptTokens,
                completionTokens,
                cost,
                WasSearchGrounded: true,
                Citations: citations);
        }, cancellationToken);
    }

    private static Dictionary<string, object?> CreateChatCompletionBody(
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        bool requireJson)
    {
        var body = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            [model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase)
                ? "max_completion_tokens"
                : "max_tokens"] = maxTokens
        };

        if (requireJson)
            body["response_format"] = new { type = "json_object" };

        return body;
    }

    private static string ExtractChatCompletionsOutputText(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static IReadOnlyList<string> ExtractChatCompletionCitationUrls(JsonElement root)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message)
            || !message.TryGetProperty("annotations", out var annotations)
            || annotations.ValueKind != JsonValueKind.Array)
            return urls.ToList();

        foreach (var annotation in annotations.EnumerateArray())
        {
            string? url = null;
            if (annotation.TryGetProperty("url", out var directUrl) && directUrl.ValueKind == JsonValueKind.String)
                url = directUrl.GetString();
            else if (annotation.TryGetProperty("url_citation", out var citation)
                     && citation.TryGetProperty("url", out var nestedUrl)
                     && nestedUrl.ValueKind == JsonValueKind.String)
                url = nestedUrl.GetString();

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https")
                urls.Add(uri.AbsoluteUri);
        }

        return urls.ToList();
    }
}
