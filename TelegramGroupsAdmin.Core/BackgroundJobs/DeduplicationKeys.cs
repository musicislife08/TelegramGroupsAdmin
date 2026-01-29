namespace TelegramGroupsAdmin.Core.BackgroundJobs;

/// <summary>
/// Centralized deduplication key generation for Quartz.NET ad-hoc jobs.
/// Use these helpers to ensure consistent key formats and prevent duplicate job scheduling.
/// </summary>
public static class DeduplicationKeys
{
    /// <summary>
    /// No deduplication - the job scheduler will generate a unique GUID.
    /// Use for jobs where each invocation should always execute (e.g., message deletion).
    /// </summary>
    public const string? None = null;

    /// <summary>
    /// Dedup key for user message cleanup jobs.
    /// Only one cleanup job per user will be scheduled at a time.
    /// </summary>
    public static string DeleteUserMessages(long userId)
        => $"DeleteUserMessages_{userId}";

    /// <summary>
    /// Dedup key for user photo fetch jobs.
    /// Prevents redundant photo fetches when user sends multiple messages.
    /// </summary>
    public static string FetchUserPhoto(long userId)
        => $"FetchUserPhoto_{userId}";

    /// <summary>
    /// Dedup key for file scan jobs.
    /// Prevents scanning the same message's file attachment twice.
    /// </summary>
    public static string FileScan(long chatId, int messageId)
        => $"FileScan_{chatId}_{messageId}";

    /// <summary>
    /// Dedup key for welcome timeout jobs.
    /// Prevents multiple timeouts for the same user joining the same chat.
    /// </summary>
    public static string WelcomeTimeout(long chatId, long userId)
        => $"WelcomeTimeout_{chatId}_{userId}";

    /// <summary>
    /// Dedup key for temp ban expiry jobs.
    /// Prevents scheduling multiple unban jobs for the same user.
    /// </summary>
    public static string TempbanExpiry(long userId)
        => $"TempbanExpiry_{userId}";
}
