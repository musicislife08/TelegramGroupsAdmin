namespace TelegramGroupsAdmin.Helpers;

/// <summary>
/// URL validation helpers to prevent open redirect vulnerabilities
/// </summary>
public static class UrlHelpers
{
    /// <summary>
    /// Validates that a URL is local (relative path starting with /) and not an open redirect
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <returns>True if the URL is a safe local path, false otherwise</returns>
    public static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // Security: Explicitly block XSS vectors (defense in depth)
        // These are also blocked by the "/" prefix check below, but explicit checks
        // protect against future refactoring that might relax the prefix requirement
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("vbscript:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Reject absolute URLs (http://, https://, //)
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        // Only allow relative URLs starting with /
        // Reject // to prevent protocol-relative URLs like //evil.com
        return url.StartsWith("/", StringComparison.Ordinal) &&
               !url.StartsWith("//", StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets a safe redirect URL, falling back to defaultUrl if the provided URL is not local
    /// </summary>
    /// <param name="url">The requested redirect URL</param>
    /// <param name="defaultUrl">The default URL to use if the requested URL is unsafe (defaults to "/")</param>
    /// <returns>A safe local URL</returns>
    public static string GetSafeRedirectUrl(string? url, string defaultUrl = "/")
    {
        return IsLocalUrl(url) ? url! : defaultUrl;
    }

    /// <summary>
    /// Builds the TOTP verification URL with properly escaped query parameters.
    /// Used for redirecting to /login/verify after password authentication.
    /// </summary>
    /// <param name="userId">The user's ID (GUID, not escaped)</param>
    /// <param name="token">The intermediate authentication token (Base64, must be escaped)</param>
    /// <param name="returnUrl">Optional return URL after successful verification</param>
    /// <param name="useRecovery">Whether to show the recovery code form instead of TOTP</param>
    /// <returns>The fully constructed verification URL</returns>
    public static string BuildVerifyUrl(string? userId, string? token, string? returnUrl = null, bool useRecovery = false)
    {
        // userId is a GUID (safe chars only), token is Base64 (needs escaping), returnUrl needs escaping
        var url = $"/login/verify?userId={userId}&token={Uri.EscapeDataString(token ?? "")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}";

        if (useRecovery)
        {
            url += "&useRecovery=true";
        }

        return url;
    }

    /// <summary>
    /// Builds the 2FA setup URL with properly escaped query parameters.
    /// Used for redirecting to /login/setup-2fa when user needs to configure TOTP.
    /// </summary>
    /// <param name="userId">The user's ID (GUID, not escaped)</param>
    /// <param name="token">The intermediate authentication token (Base64, must be escaped)</param>
    /// <param name="returnUrl">Optional return URL after successful setup</param>
    /// <param name="step">Optional step for multi-step setup flow (e.g., "recovery")</param>
    /// <returns>The fully constructed setup URL</returns>
    public static string BuildSetup2FAUrl(string? userId, string? token, string? returnUrl = null, string? step = null)
    {
        // userId is a GUID (safe chars only), token is Base64 (needs escaping), returnUrl needs escaping
        var url = $"/login/setup-2fa?userId={userId}&token={Uri.EscapeDataString(token ?? "")}&returnUrl={Uri.EscapeDataString(returnUrl ?? "")}";

        if (!string.IsNullOrEmpty(step))
        {
            url += $"&step={Uri.EscapeDataString(step)}";
        }

        return url;
    }
}
