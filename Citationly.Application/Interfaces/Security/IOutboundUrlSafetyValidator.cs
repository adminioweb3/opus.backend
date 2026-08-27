namespace Citationly.Application.Interfaces.Security;

public interface IOutboundUrlSafetyValidator
{
    Task<OutboundUrlSafetyResult> ValidateForHttpFetchAsync(
        string? url,
        bool allowMissingScheme = false,
        CancellationToken cancellationToken = default);
}

public sealed record OutboundUrlSafetyResult(
    bool IsAllowed,
    string? NormalizedUrl,
    string? Reason)
{
    public static OutboundUrlSafetyResult Allowed(string normalizedUrl) => new(true, normalizedUrl, null);
    public static OutboundUrlSafetyResult Blocked(string reason) => new(false, null, reason);
}
