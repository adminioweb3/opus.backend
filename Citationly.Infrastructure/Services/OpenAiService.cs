using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Citationly.Infrastructure.Services;

public class OpenAiService : IOpenAiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    public OpenAiService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _apiKey = ConfigPlaceholderHelper.Resolve(configuration["OpenAI:ApiKey"]) ?? string.Empty;
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public async Task<string> GenerateContentAsync(string prompt, string? systemPrompt = null, bool requireJson = false, string model = "gpt-4o-mini")
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            if (requireJson)
            {
                throw new InvalidOperationException("OpenAI is not configured; JSON analysis cannot be generated.");
            }

            return $"[Draft Generated via Fallback]\n\nBased on your recommendation, here is a generated draft expanding on: {prompt}";
        }

        await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, "openai.chat");

        var messages = new List<object>();

        if (!string.IsNullOrEmpty(systemPrompt))
        {
            messages.Add(new { role = "system", content = systemPrompt });
        }
        else
        {
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

        var requestJson = JsonSerializer.Serialize(requestBody);

        return await _aiResilience.ExecuteAsync("openai.chat", async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(75));

            var response = await _httpClient.SendAsync(request, timeoutCts.Token);
            var responseString = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"OpenAI returned {(int)response.StatusCode} ({response.StatusCode})");
                }

                throw new InvalidOperationException($"OpenAI request failed with {(int)response.StatusCode} ({response.StatusCode}): {responseString}");
            }

            using var jsonDoc = JsonDocument.Parse(responseString);
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        });
    }
}
