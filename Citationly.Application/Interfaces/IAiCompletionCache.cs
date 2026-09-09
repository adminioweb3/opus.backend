namespace Citationly.Application.Interfaces;

public interface IAiCompletionCache
{
    Task<AiProviderResult?> TryGetAsync(
        Guid? organizationId,
        string operationName,
        string providerKey,
        string systemPrompt,
        string userPrompt,
        TimeSpan freshness,
        CancellationToken cancellationToken = default);

    Task StoreAsync(
        Guid? organizationId,
        string operationName,
        string providerKey,
        string systemPrompt,
        string userPrompt,
        AiProviderResult result,
        TimeSpan freshness,
        CancellationToken cancellationToken = default);
}
