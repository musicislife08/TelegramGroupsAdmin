namespace TelegramGroupsAdmin.BackgroundJobs.Constants;

/// <summary>
/// Constants for job retry policy with exponential backoff.
/// </summary>
public static class RetryConstants
{
    /// <summary>
    /// Maximum number of retry attempts for failed jobs.
    /// Total attempts = MaxRetries + 1 (initial attempt).
    /// </summary>
    public const int MaxRetries = 3;

    /// <summary>
    /// Base backoff interval in seconds for exponential backoff calculation.
    /// Actual delay = BaseBackoffSeconds * 2^retryCount.
    /// </summary>
    public const int BaseBackoffSeconds = 10;
}
