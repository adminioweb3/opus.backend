using System.Text;
using System.Text.Json;
using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ILLMRunnerService
{
    Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null);
}

/// <summary>
/// Runs a prompt across the 3 tracked platform labels using only the org's own OpenAI key.
/// "ChatGPT" gets an unmodified GPT-4o-mini answer. "Claude" and "Gemini" are GPT-4o-mini
/// answering while instructed to respond in that platform's style, since no per-vendor API
/// keys are configured.
/// </summary>
public class LLMRunnerService : ILLMRunnerService
{
    private readonly HttpClient _httpClient;
    private readonly string _openAiKey;
    private readonly IAiRequestContextAccessor _aiContext;
    private readonly IAiUsageLimiter _aiUsageLimiter;
    private readonly IAiResilienceService _aiResilience;

    private static readonly string[] PlatformNames =
    {
        "ChatGPT", "Claude", "Gemini"
    };

    public LLMRunnerService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAiRequestContextAccessor aiContext,
        IAiUsageLimiter aiUsageLimiter,
        IAiResilienceService aiResilience)
    {
        _httpClient = httpClient;
        _openAiKey = configuration["OpenAI:ApiKey"] ?? string.Empty;
        _aiContext = aiContext;
        _aiUsageLimiter = aiUsageLimiter;
        _aiResilience = aiResilience;
    }

    public async Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null)
    {
        var tasks = PlatformNames.Select(platform => ExecuteModelAsync(analysisId, platform, promptText, ct, personaSystemPrompt));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<PromptResponse> ExecuteModelAsync(Guid analysisId, string platformName, string promptText, CancellationToken ct, string? personaSystemPrompt)
    {
        try
        {
            if (string.IsNullOrEmpty(_openAiKey))
            {
                throw new InvalidOperationException("OpenAI API key is required to generate real data. Simulated responses are disabled.");
            }

            await _aiUsageLimiter.EnsureWithinLimitsAsync(_aiContext.OrganizationId, $"prompt-intelligence:{platformName}", ct);
            string responseText = await CallOpenAiAsync(promptText, platformName, ct, personaSystemPrompt);

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

    private async Task<string> CallOpenAiAsync(string promptText, string platformName, CancellationToken ct, string? personaSystemPrompt)
    {
        string platformActing = platformName == "ChatGPT"
            ? "You are ChatGPT."
            : $"You are acting as {platformName}. Respond in a style typical of {platformName}.";
        string sysMsg = string.IsNullOrWhiteSpace(personaSystemPrompt)
            ? platformActing
            : $"{personaSystemPrompt} {platformActing}";

        var body = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = sysMsg },
                new { role = "user", content = promptText }
            },
            max_tokens = 700
        };

        return await _aiResilience.ExecuteAsync($"prompt-intelligence:{platformName}", async innerCt =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Add("Authorization", $"Bearer {_openAiKey}");
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, innerCt);
            var responseText = await response.Content.ReadAsStringAsync(innerCt);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    throw new HttpRequestException($"OpenAI call for platform '{platformName}' failed: {response.StatusCode}");
                }

                throw new InvalidOperationException($"OpenAI call for platform '{platformName}' failed: {response.StatusCode} — {responseText}");
            }

            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }, ct);
    }
}
