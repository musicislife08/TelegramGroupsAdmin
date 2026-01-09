namespace TelegramGroupsAdmin.Constants;

/// <summary>
/// Constants for notification system (in-app, email, Web Push).
/// </summary>
public static class NotificationConstants
{
    /// <summary>
    /// Default number of notifications to retrieve.
    /// </summary>
    public const int DefaultNotificationLimit = 20;

    /// <summary>
    /// Default offset for paginated notification retrieval.
    /// </summary>
    public const int DefaultNotificationOffset = 0;

    /// <summary>
    /// Maximum notifications kept in memory for state management.
    /// </summary>
    public const int MaxNotificationsInMemory = 50;

    /// <summary>
    /// Default retention period for read notifications (7 days).
    /// </summary>
    public static readonly TimeSpan DefaultNotificationRetention = TimeSpan.FromDays(7);
}
