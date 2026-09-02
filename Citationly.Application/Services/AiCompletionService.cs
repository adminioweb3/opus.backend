using System.Text.Json;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Services;

public sealed class AiCompletionService : IAiCompletionService
{
    private readonly IAiProviderRegistry _providerRegistry;
    private readonly IAiRequestContextAccessor _aiContext;

    public AiCompletionService(IAiProviderRegistry providerRegistry, IAiRequestContextAccessor aiContext)
    {
        _providerRegistry = providerRegistry;
        _aiContext = aiContext;
    }

    public async Task<AiCompletionResult> CompleteAsync(
        Guid? organizationId,
        string operationName,
        string userPrompt,
        string systemPrompt,
        bool requireJson = false,
        string? preferredProviderKey = null,
        CancellationToken cancellationToken = default)
    {
        var provider = SelectProvider(_providerRegistry.GetConfiguredProviders(), preferredProviderKey);
        if (provider is null)
        {
            var configuredProviderHint = string.IsNullOrWhiteSpace(preferredProviderKey)
                ? "No AI providers are configured."
                : $"AI provider '{preferredProviderKey}' is not configured.";
            return AiCompletionResult.Unavailable($"{configuredProviderHint} {operationName} cannot run.");
        }

        var previousOrganizationId = _aiContext.OrganizationId;
        _aiContext.OrganizationId = organizationId;
        try
        {
            var result = await provider.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
            if (requireJson && !IsJsonObject(result.Content))
            {
                return new AiCompletionResult(
                    false,
                    result.Content,
                    provider.ProviderKey,
                    provider.PlatformName,
                    result.ModelUsed,
                    result.PromptTokens,
                    result.CompletionTokens,
                    result.CostUsd,
                    result.WasSearchGrounded,
                    $"{provider.PlatformName} returned invalid JSON for {operationName}.");
            }

            return new AiCompletionResult(
                true,
                result.Content,
                provider.ProviderKey,
                provider.PlatformName,
                result.ModelUsed,
                result.PromptTokens,
                result.CompletionTokens,
                result.CostUsd,
                result.WasSearchGrounded,
                null);
        }
        catch (Exception ex)
        {
            return new AiCompletionResult(
                false,
                string.Empty,
                provider.ProviderKey,
                provider.PlatformName,
                null,
                null,
                null,
                null,
                false,
                $"{provider.PlatformName} failed for {operationName}: {ex.Message}");
        }
        finally
        {
            _aiContext.OrganizationId = previousOrganizationId;
        }
    }

    private static IAiProvider? SelectProvider(IReadOnlyList<IAiProvider> providers, string? preferredProviderKey)
    {
        if (providers.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(preferredProviderKey))
        {
            var preferred = providers.FirstOrDefault(p => string.Equals(p.ProviderKey, preferredProviderKey, StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;
        }

        return providers.FirstOrDefault(p => p.SupportsWebSearch) ?? providers[0];
    }

    private static bool IsJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[7..].Trim();
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].Trim();
        }
        else if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = trimmed[3..].Trim();
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }
}
