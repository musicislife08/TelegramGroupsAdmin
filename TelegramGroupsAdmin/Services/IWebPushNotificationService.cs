using TelegramGroupsAdmin.Constants;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing Web Push (in-app) notifications
/// Stores notifications to database and sends browser push via Web Push Protocol
/// </summary>
public interface IWebPushNotificationService
{
    /// <summary>
    /// Send an in-app notification to a user
    /// </summary>
    Task<bool> SendAsync(
        UserRecord user,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent notifications for a user
    /// </summary>
    Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = NotificationConstants.DefaultNotificationLimit,
        int offset = NotificationConstants.DefaultNotificationOffset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get unread notification count for a user
    /// </summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old read notifications (for cleanup job)
    /// </summary>
    Task<int> DeleteOldReadNotificationsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a single notification
    /// </summary>
    Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all notifications for a user
    /// </summary>
    Task DeleteAllAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get VAPID public key for browser subscription (returns null if not configured)
    /// </summary>
    Task<string?> GetVapidPublicKeyAsync(CancellationToken cancellationToken = default);
}
