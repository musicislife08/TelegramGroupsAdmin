namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for a pending notification (failed DM delivery queued for retry)
/// </summary>
public class PendingNotificationModel
{
    /// <summary>
    /// Unique identifier for this pending notification
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Telegram user ID this notification is for
    /// </summary>
    public long TelegramUserId { get; init; }

    /// <summary>
    /// Notification type (e.g., "warning", "mystatus", "welcome")
    /// </summary>
    public string NotificationType { get; init; } = string.Empty;

    /// <summary>
    /// The formatted message text to send
    /// </summary>
    public string MessageText { get; init; } = string.Empty;

    /// <summary>
    /// When this notification was first created and failed to deliver
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Number of times we've attempted to deliver this notification
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// When this notification should expire and be discarded (default 30 days)
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
