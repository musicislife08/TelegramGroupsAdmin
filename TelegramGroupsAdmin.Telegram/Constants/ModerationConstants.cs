namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for moderation thresholds and limits.
/// </summary>
public static class ModerationConstants
{
    /// <summary>
    /// Default expiry duration for warnings (90 days)
    /// </summary>
    public static readonly TimeSpan DefaultWarningExpiry = TimeSpan.FromDays(90);

    /// <summary>
    /// Minimum permission level required for admin commands (0 = regular user, 1 = admin, 2 = owner)
    /// </summary>
    public const int AdminPermissionLevel = 1;

    /// <summary>
    /// Number of chats affected when operation succeeds (single chat operations)
    /// </summary>
    public const int SingleChatSuccess = 1;

    /// <summary>
    /// Number of chats failed when operation succeeds (zero failures)
    /// </summary>
    public const int NoFailures = 0;

    /// <summary>
    /// Chat ID for global restrictions (across all managed chats)
    /// </summary>
    public const long GlobalChatId = 0;
}
