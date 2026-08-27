using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ILLMRunnerService
{
    Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null);
}

/// <summary>
/// Runs a prompt against every REAL, independently-configured AI provider (IAiProviderRegistry).
/// This used to run the same OpenAI key three times with a "respond in the style of Claude/
/// Gemini" instruction and label the result as if it came from those vendors - see
/// CITATIONLY_PRODUCT_AUDIT.md's core finding. Now: each configured provider is genuinely that
/// vendor's own API, so no "acting as X" system-prompt wrapper is needed - a provider IS its
/// platform. A provider with no API key configured is skipped entirely, not simulated.
/// </summary>
public class LLMRunnerService : ILLMRunnerService
{
    private readonly IAiProviderRegistry _providerRegistry;

    public LLMRunnerService(IAiProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public async Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null)
    {
        var providers = _providerRegistry.GetConfiguredProviders();

        if (providers.Count == 0)
        {
            return new[]
            {
                new PromptResponse
                {
                    PromptAnalysisId = analysisId,
                    Platform = "none",
                    ResponseText = "[Error] No AI providers are configured. Set at least one of OpenAI/Anthropic/Google/Perplexity's API key.",
                    ResponseLength = 0,
                    CreatedAt = DateTime.UtcNow,
                    PromptVersion = "prompt-intelligence:v1",
                    IsError = true,
                    ErrorMessage = "No AI providers are configured. Set at least one of OpenAI/Anthropic/Google/Perplexity's API key."
                }
            };
        }

        var tasks = providers.Select(provider => ExecuteProviderAsync(analysisId, provider, promptText, ct, personaSystemPrompt));
        return await Task.WhenAll(tasks);
    }

    private static async Task<PromptResponse> ExecuteProviderAsync(
        Guid analysisId,
        IAiProvider provider,
        string promptText,
        CancellationToken ct,
        string? personaSystemPrompt)
    {
        var systemPrompt = string.IsNullOrWhiteSpace(personaSystemPrompt)
            ? "You are a helpful AI assistant answering a user's question."
            : personaSystemPrompt;

        try
        {
            var result = await provider.CompleteAsync(systemPrompt, promptText, ct);
            return new PromptResponse
            {
                PromptAnalysisId = analysisId,
                Platform = provider.PlatformName,
                ResponseText = result.Content,
                ResponseLength = result.Content.Length,
                CreatedAt = DateTime.UtcNow,
                ProviderKey = provider.ProviderKey,
                ModelUsed = result.ModelUsed,
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                CostUsd = result.CostUsd,
                WasSearchGrounded = result.WasSearchGrounded,
                PromptVersion = "prompt-intelligence:v1",
                IsError = false,
            };
        }
        catch (Exception ex)
        {
            return new PromptResponse
            {
                PromptAnalysisId = analysisId,
                Platform = provider.PlatformName,
                ResponseText = $"[Error] Failed to fetch response: {ex.Message}",
                ResponseLength = 0,
                CreatedAt = DateTime.UtcNow,
                ProviderKey = provider.ProviderKey,
                PromptVersion = "prompt-intelligence:v1",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
