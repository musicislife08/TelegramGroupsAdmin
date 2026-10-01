namespace TelegramGroupsAdmin.Core.Http;

/// <summary>
/// A public-url fetch was refused or failed. <see cref="Exception.Message"/> is safe to show to
/// the user (it names no address, host or range); <see cref="Reason"/> says why, for logs.
/// </summary>
public sealed class PublicUrlFetchException(string message, string reason) : Exception(message)
{
    /// <summary>User-facing message for a URL the policy refuses: scheme, userinfo or a non-public target.</summary>
    public const string NotAllowedMessage = "That URL isn't allowed.";

    /// <summary>User-facing message for a response over the size cap.</summary>
    public static string TooLargeMessage(long maxBytes) => $"The file is larger than the {FormatSize(maxBytes)} limit.";

    private static string FormatSize(long bytes) => bytes % (1024 * 1024) == 0
        ? $"{bytes / (1024 * 1024)} MB"
        : $"{bytes / 1024} KB";

    /// <summary>User-facing message when the URL did not answer within the fetch timeout.</summary>
    public const string TimeoutMessage = "The URL did not respond in time.";

    /// <summary>User-facing message for a network-level failure (DNS, refused connection, TLS).</summary>
    public const string UnreachableMessage = "The URL could not be fetched.";

    /// <summary>Why the fetch was refused, for logs. May name the address or range; never shown to users.</summary>
    public string Reason { get; } = reason;
}
