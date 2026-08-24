namespace Citationly.Application.Interfaces;

/// <summary>
/// One real, independently-callable AI vendor. Introduced to replace the single-OpenAI-key
/// "platform simulation" the audit flagged (LLMRunnerService instructing GPT-4o-mini to
/// "respond in a style typical of Claude/Gemini") - each implementation calls its own vendor's
/// real API, and IsConfigured is false (never a fabricated fallback) until that vendor's API
/// key is actually set. See CITATIONLY_PRODUCT_AUDIT.md and
/// roadmap/PHASE_2_TRUSTWORTHY_AI_OBSERVATION.md.
/// </summary>
public interface IAiProvider
{
    /// <summary>The label shown to users and stored on PromptResponse.Platform - e.g. "ChatGPT",
    /// "Claude", "Gemini", "Perplexity". Must match this provider's real product, not a persona.</summary>
    string PlatformName { get; }

    /// <summary>A short key for storage/config lookup - e.g. "openai", "anthropic", "google",
    /// "perplexity".</summary>
    string ProviderKey { get; }

    /// <summary>False until this vendor's real API key resolves to a non-placeholder value.
    /// Callers must skip an unconfigured provider entirely rather than substituting anything.</summary>
    bool IsConfigured { get; }

    /// <summary>True if this provider's calls are grounded in a live web search by the vendor
    /// itself (e.g. Perplexity's native search), not a plain parametric-knowledge completion.</summary>
    bool SupportsWebSearch { get; }

    Task<AiProviderResult> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

public sealed record AiProviderResult(
    string Content,
    string ModelUsed,
    int? PromptTokens,
    int? CompletionTokens,
    decimal? CostUsd,
    bool WasSearchGrounded);

public interface IAiProviderRegistry
{
    /// <summary>Providers whose real API key is present - the only ones ever called for a live
    /// prompt execution.</summary>
    IReadOnlyList<IAiProvider> GetConfiguredProviders();

    /// <summary>All registered providers regardless of configuration, for status/disclosure UI
    /// (e.g. "Claude - not connected yet").</summary>
    IReadOnlyList<IAiProvider> GetAllProviders();
}
