using System.Net;

namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// What a browser push subscription endpoint must look like before the server will store it or
/// POST to it: an absolute https URL without userinfo whose host, when it is an IP literal, is a
/// public address under <see cref="PublicAddressPolicy"/>. Hostnames are not resolved here; the
/// push client's connections run on the public-url handler, which judges the resolved address at
/// connect time.
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
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal)
            && !PublicAddressPolicy.IsPublic(literal, out var rangeReason))
        {
            reason = $"{literal} is {rangeReason}";
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
