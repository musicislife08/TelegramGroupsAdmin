namespace TelegramGroupsAdmin.Telegram.Constants;

/// <summary>
/// Centralized constants for notification formatting and display.
/// </summary>
public static class NotificationConstants
{
    /// <summary>
    /// Maximum message text preview length for notifications (200 characters)
    /// Used to truncate long messages in consolidated spam notifications
    /// </summary>
    public const int MessagePreviewMaxLength = 200;

    /// <summary>
    /// Preview truncation offset - characters to remove from end before adding ellipsis (3 characters for "...")
    /// </summary>
    public const int PreviewTruncationOffset = 3;

    /// <summary>
    /// Maximum number of detection checks to include in notification (top 3)
    /// </summary>
    public const int MaxDetectionChecksInNotification = 3;
}
