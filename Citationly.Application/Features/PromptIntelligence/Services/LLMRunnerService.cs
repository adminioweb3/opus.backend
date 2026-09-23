using Citationly.Application.Interfaces;
using Citationly.Domain.Entities;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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
    public const int LegacySamplesPerProvider = 3;
    public const int SamplesPerProvider = LegacySamplesPerProvider;
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly int _samplesPerProvider;

    public LLMRunnerService(IAiProviderRegistry providerRegistry, IAiCompletionCache completionCache)
        : this(providerRegistry, completionCache, LegacySamplesPerProvider)
    {
    }

    public LLMRunnerService(
        IAiProviderRegistry providerRegistry,
        IAiCompletionCache completionCache,
        IConfiguration configuration)
        : this(providerRegistry, completionCache, Math.Clamp(configuration.GetValue("OpenAI:ObservationSamples", 1), 1, 5))
    {
    }

    private LLMRunnerService(IAiProviderRegistry providerRegistry, IAiCompletionCache completionCache, int samplesPerProvider)
    {
        _providerRegistry = providerRegistry;
        _samplesPerProvider = samplesPerProvider;
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
                    ResponseText = "[Error] OpenAI is not configured. Set OpenAI:ApiKey.",
                    ResponseLength = 0,
                    CreatedAt = DateTime.UtcNow,
                    PromptVersion = "prompt-intelligence:v1",
                    IsError = true,
                    ErrorMessage = "OpenAI is not configured. Set OpenAI:ApiKey."
                }
            };
        }

        var tasks = providers.SelectMany(provider =>
            Enumerable.Range(1, _samplesPerProvider)
                .Select(_ => ExecuteProviderAsync(
                    analysisId,
                    provider,
                    promptText,
                    ct,
                    personaSystemPrompt)));
        return await Task.WhenAll(tasks);
    }

    private async Task<PromptResponse> ExecuteProviderAsync(
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
            // Visibility is a time-sensitive observation. A cached completion would make a
            // user-triggered rerun appear current while silently replaying an older answer.
            var result = provider.SupportsWebSearch
                ? await provider.CompleteWithWebSearchAsync(systemPrompt, promptText, ct)
                : await provider.CompleteAsync(systemPrompt, promptText, ct);

            return new PromptResponse
            {
                Id = Guid.NewGuid(),
                PromptAnalysisId = analysisId,
                Platform = result.WasSearchGrounded &&
                           provider.ProviderKey.Equals("openai", StringComparison.OrdinalIgnoreCase)
                    ? "OpenAI Search API"
                    : provider.PlatformName,
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
                Gateway = result.Gateway,
                UpstreamProvider = result.UpstreamProvider,
                GenerationId = result.GenerationId,
                LatencyMs = result.LatencyMs,
                PromptVersion = "prompt-intelligence:v3-search-grounded-sampled",
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
                PromptVersion = "prompt-intelligence:v3-search-grounded-sampled",
                IsError = true,
                ErrorMessage = ex.Message
            };
        }
    }
}
