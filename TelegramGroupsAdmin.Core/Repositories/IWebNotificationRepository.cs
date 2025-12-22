using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Repositories;

/// <summary>
/// Repository for in-app web notifications
/// Manages the web_notifications table for Web Push channel
/// </summary>
public interface IWebNotificationRepository
{
    /// <summary>
    /// Create a new notification
    /// </summary>
    Task<WebNotification> CreateAsync(WebNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent notifications for a user (paginated, ordered by created_at DESC)
    /// </summary>
    Task<IReadOnlyList<WebNotification>> GetRecentAsync(string userId, int limit = 20, int offset = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get unread notification count for a user
    /// </summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark a single notification as read
    /// </summary>
    Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete old read notifications (retention cleanup)
    /// Returns number of deleted notifications
    /// </summary>
    Task<int> DeleteOldReadNotificationsAsync(TimeSpan retention, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a single notification
    /// </summary>
    Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all notifications for a user
    /// </summary>
    Task DeleteAllAsync(string userId, CancellationToken cancellationToken = default);
}
