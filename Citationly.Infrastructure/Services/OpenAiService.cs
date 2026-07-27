using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Citationly.Application.Interfaces;

namespace Citationly.Infrastructure.Services;

public class OpenAiService : IOpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public OpenAiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
    }

    public async Task<string> GenerateContentAsync(string prompt, string? systemPrompt = null, bool requireJson = false, string model = "gpt-4o-mini")
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

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        var requestJson = JsonSerializer.Serialize(requestBody);

        // A single slow/degraded call used to be able to ride the client's full 10-minute
        // timeout with no recovery. Bound each attempt to 75s and retry once on timeout, 429, or
        // a 5xx — most transient OpenAI slowness clears on retry well inside that window, and a
        // second attempt costs far less than silently waiting out the global ceiling.
        HttpResponseMessage response;
        try
        {
            response = await PostWithTimeoutAsync(requestJson, TimeSpan.FromSeconds(75));
        }
        catch (TaskCanceledException)
        {
            response = await PostWithTimeoutAsync(requestJson, TimeSpan.FromSeconds(75));
        }

        if (!response.IsSuccessStatusCode &&
            (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
        {
            await Task.Delay(2000);
            response = await PostWithTimeoutAsync(requestJson, TimeSpan.FromSeconds(75));
        }

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

    private async Task<HttpResponseMessage> PostWithTimeoutAsync(string requestJson, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content, cts.Token);
    }
}
