namespace TelegramGroupsAdmin.Core.Http;

/// <summary>Why a public-url fetch did not return a body.</summary>
public enum PublicUrlFetchFailure
{
    /// <summary>The policy refused the URL: scheme, userinfo, a non-public target, or too many redirects.</summary>
    NotAllowed,

    /// <summary>The body exceeded the caller's cap.</summary>
    TooLarge,

    /// <summary>The fetch did not complete within its deadline.</summary>
    Timeout,

    /// <summary>DNS, connection or TLS failed, or the body could not be read.</summary>
    Unreachable,

    /// <summary>The server answered with a non-success status.</summary>
    HttpStatus,

    /// <summary>
    /// A redirect hop would have left https for http. Still a refusal (<see cref="PublicUrlFetchException.NotAllowedMessage"/>
    /// is its user-facing message), but distinguishable so a caller that was itself asked to fetch plain http can decide to.
    /// </summary>
    DowngradeRefused,
}

/// <summary>
/// A public-url fetch was refused or failed. <see cref="Exception.Message"/> is safe to show to
/// the user (it names no address, host or range); <see cref="Reason"/> says why, for logs.
/// </summary>
public sealed class PublicUrlFetchException(PublicUrlFetchFailure kind, string message, string reason) : Exception(message)
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

    /// <summary>Which class of failure this is, for callers that react differently to a policy refusal than to a flaky server.</summary>
    public PublicUrlFetchFailure Kind { get; } = kind;

    /// <summary>Why the fetch was refused, for logs. May name the address or range; never shown to users.</summary>
    public string Reason { get; } = reason;
}
