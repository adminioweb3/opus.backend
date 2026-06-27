using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public class OpenRouterService : IOpenRouterService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    // Static semaphore to limit all OpenRouter calls to 1 concurrent request across the app
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public OpenRouterService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenRouter:ApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateContentAsync(string prompt, string? systemPrompt = null, bool requireJson = false, string model = "openai/gpt-3.5-turbo")
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return $"[Draft Generated via Fallback]\n\nBased on your recommendation, here is a generated draft expanding on: {prompt}";
        }

        var messages = new List<object>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        else
        {
            // Default legacy system prompt
            messages.Add(new { role = "system", content = "You are an expert SEO content writer. Expand the user's brief recommendation into a full, detailed blog post or page draft of at least 300 words. Output pure text/markdown, no meta commentary." });
        }

        messages.Add(new { role = "user", content = prompt });

        object requestBody;
        if (requireJson)
        {
            requestBody = new
            {
                model = model,
                response_format = new { type = "json_object" },
                messages = messages.ToArray(),
                max_tokens = 16000
            };
        }
        else
        {
            requestBody = new
            {
                model = model,
                messages = messages.ToArray()
            };
        }

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5173");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "Citationly");

        HttpResponseMessage? response = null;
        int maxRetries = 10; // Increased max retries

        await _semaphore.WaitAsync();
        try
        {
            for (int i = 0; i < maxRetries; i++)
            {
                response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);
                if (response.IsSuccessStatusCode)
                {
                    break;
                }
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (i == maxRetries - 1) break; // Don't delay on the last attempt
                    // Add some jitter to the backoff delay
                    var jitter = new Random().Next(500, 1500);
                    await Task.Delay((2000 * (i + 1)) + jitter);
                }
                else
                {
                    break; // Stop retrying on other errors like 400, 401, 404, etc.
                }
            }

            // Add a mandatory delay after success to prevent hitting the rate limit immediately on the next call
            if (response.IsSuccessStatusCode)
            {
                await Task.Delay(2500);
            }
        }
        finally
        {
            _semaphore.Release();
        }

        response!.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(responseString);
        var messageContent = jsonDoc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return messageContent ?? string.Empty;
    }
}
