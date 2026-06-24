using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Opus.Application.Interfaces;

namespace Opus.Infrastructure.Services;

public class OpenRouterService : IOpenRouterService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenRouterService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenRouter:ApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateContentAsync(string prompt)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            // Fallback for development if no key is provided
            return $"[Draft Generated via Fallback]\n\nBased on your recommendation, here is a generated draft expanding on: {prompt}";
        }

        var requestBody = new
        {
            model = "openai/gpt-3.5-turbo", // You can switch this to any OpenRouter model
            messages = new[]
            {
                new { role = "system", content = "You are an expert SEO content writer. Expand the user's brief recommendation into a full, detailed blog post or page draft of at least 300 words. Output pure text/markdown, no meta commentary." },
                new { role = "user", content = prompt }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5173");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "Opus");

        var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();

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
