namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for Telegram bot connection and retry logic.
/// </summary>
public static class ConnectionConstants
{
    /// <summary>
    /// Maximum retry delay in seconds for exponential backoff (60 seconds)
    /// </summary>
    public const int MaxRetryDelaySeconds = 60;

    /// <summary>
    /// Base for exponential backoff calculation (2^attempt)
    /// </summary>
    public const double ExponentialBackoffBase = 2.0;

    /// <summary>
    /// Initial connection attempt offset for logging (1-indexed)
    /// Used to convert 0-based attempt count to 1-based for user-friendly display
    /// </summary>
    public const int ConnectionAttemptOffset = 1;
}
