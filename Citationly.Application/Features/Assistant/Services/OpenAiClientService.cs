using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Citationly.Application.Features.Assistant.Services;

public class OpenAiClientService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;

    public OpenAiClientService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateResponseFastAsync(string prompt, CancellationToken ct)
    {
        // Use a faster model for intent detection
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
                return "{\"intent\":\"General Chat\",\"requiredTools\":[]}"; // Mock fallback for intent

            return "This is a mock AI response. Please configure your OpenAI API Key in `appsettings.json` to enable real AI generation.";
        }

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        var payload = new
        {
            model = model,
            max_tokens = maxTokens,
            messages = messages
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new Exception($"OpenRouter API Error: {err}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? string.Empty;
    }
}
