namespace Citationly.Application.Interfaces;

/// <summary>
/// Independent web evidence retrieval. These results do not prove that a consumer AI product
/// cited the returned pages.
/// </summary>
public interface IWebEvidenceProvider
{
    string ProviderKey { get; }
    bool IsConfigured { get; }

    Task<WebEvidenceResult> SearchAsync(
        Guid? organizationId,
        WebEvidenceQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record WebEvidenceQuery(
    string Query,
    int ResultCount = 5,
    string? Country = null,
    string? Language = null,
    DateTimeOffset? PublishedAfter = null);

public sealed record WebEvidenceItem(
    string Url,
    string Title,
    string? PublishedDate,
    string? Author,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<double> HighlightScores);

public sealed record WebEvidenceResult(
    bool Success,
    string ProviderKey,
    string? RequestId,
    IReadOnlyList<WebEvidenceItem> Items,
    decimal? CostUsd,
    bool CacheHit,
    string? ErrorMessage)
{
    public static WebEvidenceResult Unavailable(string providerKey, string message) =>
        new(false, providerKey, null, Array.Empty<WebEvidenceItem>(), null, false, message);
}
