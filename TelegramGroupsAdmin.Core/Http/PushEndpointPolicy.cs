using System.Net;

namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// What a browser push subscription endpoint must look like before the server will store it or
/// POST to it: an absolute https URL without userinfo whose host is either a public IP literal
/// under <see cref="PublicAddressPolicy"/> or a fully qualified name (not localhost, not a
/// single label). Hostnames are not resolved here; the push client's connections run on the
/// public-url handler, which judges the resolved address at connect time.
/// </summary>
public static class PushEndpointPolicy
{
    /// <summary>User-facing message for a rejected endpoint; names no address.</summary>
    public const string RejectedMessage = "That push endpoint isn't allowed.";

    /// <summary>True when <paramref name="endpoint"/> may be stored and pushed to; otherwise <paramref name="reason"/> says why not (for logs).</summary>
    public static bool IsAcceptable(string? endpoint, out string reason)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            reason = "not an absolute URL";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"scheme '{uri.Scheme}' is not https";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "URL carries userinfo";
            return false;
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            reason = "URL has no host";
            return false;
        }

        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal))
        {
            if (!PublicAddressPolicy.IsPublic(literal, out var rangeReason))
            {
                reason = $"{literal} is {rangeReason}";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // Real push services (FCM, Mozilla, Apple, WNS) are fully qualified names. "localhost",
        // anything under ".localhost" (RFC 6761) and single-label names only ever mean the local
        // machine or the local network.
        var host = uri.Host.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"host '{uri.Host}' is localhost";
            return false;
        }

        if (!host.Contains('.'))
        {
            reason = $"host '{uri.Host}' is a single-label name";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Throws <see cref="PushEndpointRejectedException"/> unless <see cref="IsAcceptable"/>.</summary>
    public static void EnsureAcceptable(string? endpoint)
    {
        if (!IsAcceptable(endpoint, out var reason))
            throw new PushEndpointRejectedException(reason);
    }
}

/// <summary>
/// A push endpoint failed <see cref="PushEndpointPolicy"/>. <see cref="Exception.Message"/> is
/// safe to show; <see cref="Reason"/> may name the address and is for logs.
/// </summary>
public sealed class PushEndpointRejectedException(string reason) : Exception(PushEndpointPolicy.RejectedMessage)
{
    public string Reason { get; } = reason;
}
