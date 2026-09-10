using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Citationly.Application.Interfaces;

namespace Citationly.Application.Services;

public sealed class InMemoryAiCompletionCache : IAiCompletionCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public Task<AiProviderResult?> TryGetAsync(
        Guid? organizationId,
        string operationName,
        string providerKey,
        string systemPrompt,
        string userPrompt,
        TimeSpan freshness,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(organizationId, operationName, providerKey, systemPrompt, userPrompt, freshness);
        if (!_entries.TryGetValue(key, out var entry)) return Task.FromResult<AiProviderResult?>(null);
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return Task.FromResult<AiProviderResult?>(null);
        }

        var cached = entry.Result;
        return Task.FromResult<AiProviderResult?>(new AiProviderResult(
            cached.Content,
            cached.ModelUsed,
            PromptTokens: null,
            CompletionTokens: null,
            CostUsd: null,
            cached.WasSearchGrounded,
            cached.Citations));
    }

    public Task StoreAsync(
        Guid? organizationId,
        string operationName,
        string providerKey,
        string systemPrompt,
        string userPrompt,
        AiProviderResult result,
        TimeSpan freshness,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(result.Content)) return Task.CompletedTask;

        var key = BuildKey(organizationId, operationName, providerKey, systemPrompt, userPrompt, freshness);
        _entries[key] = new CacheEntry(
            new AiProviderResult(
                result.Content,
                result.ModelUsed,
                result.PromptTokens,
                result.CompletionTokens,
                result.CostUsd,
                result.WasSearchGrounded,
                result.Citations),
            DateTimeOffset.UtcNow.Add(freshness));

        return Task.CompletedTask;
    }

    private static string BuildKey(
        Guid? organizationId,
        string operationName,
        string providerKey,
        string systemPrompt,
        string userPrompt,
        TimeSpan freshness)
    {
        var bucketSeconds = Math.Max(60, (int)freshness.TotalSeconds);
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / bucketSeconds;
        var raw = string.Join('\n',
            organizationId?.ToString("N") ?? "global",
            operationName.Trim().ToLowerInvariant(),
            providerKey.Trim().ToLowerInvariant(),
            bucket.ToString(),
            systemPrompt.Trim(),
            userPrompt.Trim());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private sealed record CacheEntry(AiProviderResult Result, DateTimeOffset ExpiresAt);
}
