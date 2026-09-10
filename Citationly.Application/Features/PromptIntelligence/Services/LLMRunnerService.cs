using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text.Json;

namespace Citationly.Application.Features.PromptIntelligence.Services;

public interface ILLMRunnerService
{
    Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid organizationId, Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null);
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
    public const int SamplesPerProvider = 3;
    private static readonly TimeSpan CacheFreshness = TimeSpan.FromHours(6);
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAiCompletionCache _completionCache;

    public LLMRunnerService(IAiProviderRegistry providerRegistry, IAiCompletionCache completionCache)
    {
        _providerRegistry = providerRegistry;
        _completionCache = completionCache;
    }

    public async Task<IEnumerable<PromptResponse>> RunPromptAcrossModelsAsync(Guid organizationId, Guid analysisId, string promptText, CancellationToken ct, string? personaSystemPrompt = null)
    {
        var providers = _providerRegistry.GetConfiguredProviders();

        if (providers.Count == 0)
        {
            return new[]
            {
                new PromptResponse
                {
                    Id = Guid.NewGuid(),
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

        var tasks = providers.SelectMany(provider =>
            Enumerable.Range(1, SamplesPerProvider)
                .Select(sampleIndex => ExecuteProviderAsync(
                    organizationId,
                    analysisId,
                    provider,
                    promptText,
                    sampleIndex,
                    ct,
                    personaSystemPrompt)));
        return await Task.WhenAll(tasks);
    }

    private async Task<PromptResponse> ExecuteProviderAsync(
        Guid organizationId,
        Guid analysisId,
        IAiProvider provider,
        string promptText,
        int sampleIndex,
        CancellationToken ct,
        string? personaSystemPrompt)
    {
        var systemPrompt = string.IsNullOrWhiteSpace(personaSystemPrompt)
            ? "You are a helpful AI assistant answering a user's question."
            : personaSystemPrompt;

        try
        {
            var result = await _completionCache.TryGetAsync(
                organizationId,
                operationName: $"prompt-intelligence.analysis.sample-{sampleIndex}",
                provider.ProviderKey,
                systemPrompt,
                promptText,
                CacheFreshness,
                ct);

            if (result == null)
            {
                result = await provider.CompleteAsync(systemPrompt, promptText, ct);
                await _completionCache.StoreAsync(
                    organizationId,
                    operationName: $"prompt-intelligence.analysis.sample-{sampleIndex}",
                    provider.ProviderKey,
                    systemPrompt,
                    promptText,
                    result,
                    CacheFreshness,
                    ct);
            }

            return new PromptResponse
            {
                Id = Guid.NewGuid(),
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
                SourceUrlsJson = JsonSerializer.Serialize(result.Citations ?? Array.Empty<string>()),
                PromptVersion = "prompt-intelligence:v2-sampled",
                IsError = false,
            };
        }
        catch (Exception ex)
        {
            return new PromptResponse
            {
                Id = Guid.NewGuid(),
                PromptAnalysisId = analysisId,
                Platform = provider.PlatformName,
                ResponseText = $"[Error] Failed to fetch response: {ex.Message}",
                ResponseLength = 0,
                CreatedAt = DateTime.UtcNow,
                ProviderKey = provider.ProviderKey,
                PromptVersion = "prompt-intelligence:v2-sampled",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
