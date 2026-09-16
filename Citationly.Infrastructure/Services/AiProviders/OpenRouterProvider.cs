using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services.AiProviders;

public sealed record OpenRouterModelDefinition(
    string Key,
    string PlatformLabel,
    string Model,
    bool Enabled,
    int MaxOutputTokens);

/// <summary>
/// One pinned model exposed through OpenRouter. Each configured model is registered as a
/// separate IAiProvider so longitudinal scans never silently switch model families.
/// </summary>
public sealed class OpenRouterProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterModelDefinition _definition;
    private readonly string? _apiKey;
    private readonly string _baseUrl;
    private readonly string _applicationName;
    private readonly string? _applicationUrl;
    private readonly string _dataCollection;
    private readonly bool _requireZdr;
    private readonly decimal _reservedCostPerCallUsd;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public OpenRouterProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        OpenRouterModelDefinition definition,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _definition = definition;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["OpenRouter:ApiKey"], "OPENROUTER_API_KEY");
        _baseUrl = (configuration["OpenRouter:BaseUrl"] ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        _applicationName = configuration["OpenRouter:ApplicationName"] ?? "Citationly";
        _applicationUrl = ConfigPlaceholderHelper.Resolve(configuration["OpenRouter:ApplicationUrl"]);
        _dataCollection = configuration["OpenRouter:DataCollection"] ?? "deny";
        _requireZdr = configuration.GetValue("OpenRouter:RequireZdr", true);
        _reservedCostPerCallUsd = Math.Max(0.000001m, configuration.GetValue("OpenRouter:ReservedCostPerCallUsd", 0.02m));
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public string PlatformName => _definition.PlatformLabel;
    public string ProviderKey => $"openrouter:{_definition.Key}";
    public bool IsConfigured => _definition.Enabled && _apiKey is not null && !string.IsNullOrWhiteSpace(_definition.Model);
    public bool SupportsWebSearch => false;

    public async Task<AiProviderResult> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException($"OpenRouter model '{_definition.Key}' is not configured.");

        var operation = $"provider:{ProviderKey}";
        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, operation, cancellationToken);
        await _aiUsageLimiter.RecordEstimatedCostAsync(
            _aiContext.OrganizationId,
            _reservedCostPerCallUsd,
            operation,
            cancellationToken);

        var body = new
        {
            model = _definition.Model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = _definition.MaxOutputTokens,
            provider = new
            {
                allow_fallbacks = false,
                data_collection = _dataCollection,
                zdr = _requireZdr
            }
        };

        return await _aiResilience.ExecuteAsync(operation, async ct =>
        {
            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.TryAddWithoutValidation("X-Title", _applicationName);
            if (_applicationUrl is not null)
                request.Headers.TryAddWithoutValidation("HTTP-Referer", _applicationUrl);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    throw new HttpRequestException($"OpenRouter returned {(int)response.StatusCode} for {_definition.Key}.");

                throw new InvalidOperationException($"OpenRouter returned {(int)response.StatusCode} for {_definition.Key}.");
            }

            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;
            var message = root.GetProperty("choices")[0].GetProperty("message");
            var content = message.TryGetProperty("content", out var contentElement)
                ? contentElement.GetString() ?? string.Empty
                : string.Empty;
            var modelUsed = root.TryGetProperty("model", out var modelElement)
                ? modelElement.GetString() ?? _definition.Model
                : _definition.Model;
            var generationId = root.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var upstreamProvider = root.TryGetProperty("provider", out var providerElement) ? providerElement.GetString() : null;

            int? promptTokens = null;
            int? completionTokens = null;
            decimal? cost = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens = usage.TryGetProperty("prompt_tokens", out var prompt) ? prompt.GetInt32() : null;
                completionTokens = usage.TryGetProperty("completion_tokens", out var completion) ? completion.GetInt32() : null;
                cost = usage.TryGetProperty("cost", out var costElement) && costElement.TryGetDecimal(out var parsedCost)
                    ? parsedCost
                    : null;
            }

            var citations = ExtractCitations(message);
            if (cost.HasValue && cost.Value > _reservedCostPerCallUsd)
            {
                await _aiUsageLimiter.RecordEstimatedCostAsync(
                    _aiContext.OrganizationId,
                    cost.Value - _reservedCostPerCallUsd,
                    operation,
                    ct);
            }

            return new AiProviderResult(
                content,
                modelUsed,
                promptTokens,
                completionTokens,
                cost,
                WasSearchGrounded: citations.Count > 0,
                Citations: citations,
                Gateway: "openrouter",
                UpstreamProvider: upstreamProvider,
                GenerationId: generationId,
                LatencyMs: stopwatch.ElapsedMilliseconds);
        }, cancellationToken);
    }

    private static IReadOnlyList<string> ExtractCitations(JsonElement message)
    {
        if (!message.TryGetProperty("annotations", out var annotations) || annotations.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var annotation in annotations.EnumerateArray())
        {
            if (annotation.TryGetProperty("url", out var directUrl) && directUrl.ValueKind == JsonValueKind.String)
                urls.Add(directUrl.GetString()!);
            if (annotation.TryGetProperty("url_citation", out var citation)
                && citation.TryGetProperty("url", out var nestedUrl)
                && nestedUrl.ValueKind == JsonValueKind.String)
                urls.Add(nestedUrl.GetString()!);
        }
        return urls.ToList();
    }
}
