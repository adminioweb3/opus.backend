using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ILLMRunnerService
{
    Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct);
}

public class LLMRunnerService : ILLMRunnerService
{
    private readonly HttpClient _httpClient;
    private readonly string _openRouterKey;
    private readonly string _openAiKey;

    public LLMRunnerService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _openRouterKey = configuration["OpenRouter:ApiKey"] ?? string.Empty;
        _openAiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
    }

    public async Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct)
    {
        var configuredModels = new Dictionary<string, string>
        {
            { "ChatGPT", "openai/gpt-4o" },
            { "Claude", "anthropic/claude-3-opus" },
            { "Gemini", "google/gemini-pro-1.5" },
            { "Perplexity", "perplexity/llama-3-sonar-large-32k-online" },
            { "Mistral", "mistralai/mixtral-8x7b-instruct" },
            { "Grok", "x-ai/grok-2" },
            { "DeepSeek", "deepseek/deepseek-coder" }
        };

        var tasks = new List<Task<PromptResponse>>();

        foreach (var kvp in configuredModels)
        {
            tasks.Add(ExecuteModelAsync(analysisId, kvp.Key, kvp.Value, promptText, ct));
        }

        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<PromptResponse> ExecuteModelAsync(Guid analysisId, string platformName, string modelId, string promptText, CancellationToken ct)
    {
        try
        {
            string responseText = string.Empty;

            if (!string.IsNullOrEmpty(_openRouterKey))
            {
                // Use OpenRouter for actual multi-model
                responseText = await CallOpenRouterAsync(modelId, promptText, ct);
            }
            else if (!string.IsNullOrEmpty(_openAiKey))
            {
                // Fallback: simulate multi-model using OpenAI if OpenRouter is disabled
                string fakeModel = platformName == "ChatGPT" ? "gpt-4o" : "gpt-4o-mini";
                responseText = await CallOpenAiAsync(fakeModel, promptText, platformName, ct);
            }
            else
            {
                // Absolute fallback mock
                responseText = $"[Simulated Response from {platformName}]\nThis is a simulated response to: {promptText}";
            }

            return new PromptResponse
            {
                PromptAnalysisId = analysisId,
                Platform = platformName,
                ResponseText = responseText,
                ResponseLength = responseText.Length,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new PromptResponse
            {
                PromptAnalysisId = analysisId,
                Platform = platformName,
                ResponseText = $"[Error] Failed to fetch response: {ex.Message}",
                ResponseLength = 0,
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<string> CallOpenRouterAsync(string modelId, string promptText, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_openRouterKey}");

        var body = new
        {
            model = modelId,
            messages = new[] { new { role = "user", content = promptText } }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    private async Task<string> CallOpenAiAsync(string modelId, string promptText, string platformName, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {_openAiKey}");

        // For non-ChatGPT platforms, ask OpenAI to act like them for a realistic simulation without credits
        string sysMsg = platformName == "ChatGPT"
            ? "You are ChatGPT."
            : $"You are acting as {platformName}. Respond in a style typical of {platformName}.";

        var body = new
        {
            model = modelId,
            messages = new[]
            {
                new { role = "system", content = sysMsg },
                new { role = "user", content = promptText }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return $"[API Error from OpenAI acting as {platformName}]";

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
