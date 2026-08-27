namespace Citationly.Application.Interfaces;

public interface IAiCompletionService
{
    Task<AiCompletionResult> CompleteAsync(
        Guid? organizationId,
        string operationName,
        string userPrompt,
        string systemPrompt,
        bool requireJson = false,
        string? preferredProviderKey = null,
        CancellationToken cancellationToken = default);
}

public sealed record AiCompletionResult(
    bool Success,
    string Content,
    string? ProviderKey,
    string? Platform,
    string? ModelUsed,
    int? PromptTokens,
    int? CompletionTokens,
    decimal? CostUsd,
    bool WasSearchGrounded,
    string? ErrorMessage)
{
    public static AiCompletionResult Unavailable(string message) =>
        new(false, string.Empty, null, null, null, null, null, null, false, message);
}
