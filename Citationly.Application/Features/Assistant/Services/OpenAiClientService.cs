using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Application.Features.Assistant.Services;

public class OpenAiClientService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _fastModel;
    private readonly string _chatModel;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public OpenAiClientService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = ResolveConfiguredSecret(configuration["OpenAI:ApiKey"])
            ?? ResolveConfiguredSecret(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
            ?? string.Empty;
        _baseUrl = "https://api.openai.com/v1";
        _fastModel = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        _chatModel = configuration["OpenAI:ChatModel"] ?? "gpt-4o";
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public async Task<string> GenerateResponseFastAsync(string prompt, CancellationToken ct)
    {
        var messages = new List<object>
        {
            new { role = "user", content = prompt }
        };

        return await CallOpenAiAsync(messages, _fastModel, 500, ct, isIntent: true);
    }

    public async Task<string> GenerateResponseAsync(object messageList, CancellationToken ct)
    {
        return await CallOpenAiAsync(messageList, _chatModel, 1600, ct);
    }

    private async Task<string> CallOpenAiAsync(object messages, string model, int maxTokens, CancellationToken ct, bool isIntent = false)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            if (isIntent)
                return "{\"intent\":\"General Chat\",\"requiredTools\":[]}";

            throw new InvalidOperationException("OpenAI is not configured; assistant responses cannot be generated.");
        }

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, isIntent ? "assistant.intent" : "assistant.chat", ct);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["messages"] = messages
        };
        return await _aiResilience.ExecuteAsync(isIntent ? "assistant.intent" : "assistant.chat", async innerCt =>
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, innerCt);
            var responseBody = await response.Content.ReadAsStringAsync(innerCt);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"OpenAI request failed with status {response.StatusCode}.");
                }

                throw new InvalidOperationException($"OpenAI request failed with status {response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }, ct);
    }

    private static string? ResolveConfiguredSecret(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value == "YOUR_OPENAI_API_KEY") return null;
        return value.StartsWith("${", StringComparison.Ordinal) ? null : value;
    }
}
