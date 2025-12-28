namespace TelegramGroupsAdmin.Ui.Helpers;

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
}
