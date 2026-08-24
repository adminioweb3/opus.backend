using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Application.Features.Assistant.Services;

public class OpenAiClientService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
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
        _apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
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

        return await CallOpenRouterAsync(messages, "gpt-4o-mini", 500, ct, isIntent: true);
    }

    public async Task<string> GenerateResponseAsync(object messageList, CancellationToken ct)
    {
        return await CallOpenRouterAsync(messageList, "gpt-4o", 1000, ct);
    }

    private async Task<string> CallOpenRouterAsync(object messages, string model, int maxTokens, CancellationToken ct, bool isIntent = false)
    {
        if (string.IsNullOrEmpty(_apiKey) || _apiKey == "YOUR_OPENAI_API_KEY")
        {
            if (isIntent)
                return "{\"intent\":\"General Chat\",\"requiredTools\":[]}";

            return "This is a mock AI response. Please configure your OpenAI API Key in `appsettings.json` to enable real AI generation.";
        }

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, isIntent ? "assistant.intent" : "assistant.chat", ct);

        var payload = new
        {
            model = model,
            max_tokens = maxTokens,
            messages = messages
        };

        return await _aiResilience.ExecuteAsync(isIntent ? "assistant.intent" : "assistant.chat", async innerCt =>
        {
            var httpClient = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, innerCt);
            var responseBody = await response.Content.ReadAsStringAsync(innerCt);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"OpenRouter API Error: {response.StatusCode}");
                }

                throw new InvalidOperationException($"OpenRouter API Error: {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }, ct);
    }
}
