using System.Net;
using System.Net.Sockets;
using Citationly.Application.Interfaces.Security;

namespace Citationly.Application.Security;

public sealed class OutboundUrlSafetyValidator : IOutboundUrlSafetyValidator
{
    private static readonly HashSet<string> BlockedHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "localhost.localdomain",
        "metadata.google.internal"
    };

    public async Task<OutboundUrlSafetyResult> ValidateForHttpFetchAsync(
        string? url,
        bool allowMissingScheme = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return OutboundUrlSafetyResult.Blocked("URL is required.");
        }

        var candidate = url.Trim();
        if (allowMissingScheme && !candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return OutboundUrlSafetyResult.Blocked("URL must be absolute.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return OutboundUrlSafetyResult.Blocked("Only HTTP and HTTPS URLs are allowed.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return OutboundUrlSafetyResult.Blocked("URL host is required.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return OutboundUrlSafetyResult.Blocked("URLs with embedded credentials are not allowed.");
        }

        if (BlockedHostNames.Contains(uri.Host.TrimEnd('.')))
        {
            return OutboundUrlSafetyResult.Blocked("Internal host names are not allowed.");
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literalAddress))
        {
            addresses = new[] { literalAddress };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch
            {
                return OutboundUrlSafetyResult.Blocked("URL host could not be resolved.");
            }
        }

        if (addresses.Length == 0)
        {
            return OutboundUrlSafetyResult.Blocked("URL host could not be resolved.");
        }

        if (addresses.Any(IsBlockedAddress))
        {
            return OutboundUrlSafetyResult.Blocked("URL resolves to a private or reserved network address.");
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Host = uri.IdnHost.ToLowerInvariant()
        };

        return OutboundUrlSafetyResult.Allowed(builder.Uri.ToString());
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] switch
            {
                0 => true,
                10 => true,
                100 when bytes[1] is >= 64 and <= 127 => true,
                127 => true,
                169 when bytes[1] == 254 => true,
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                198 when bytes[1] is 18 or 19 => true,
                >= 224 => true,
                _ => false
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
            {
                return true;
            }

            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xfe) == 0xfc;
        }

        return true;
    }
}
