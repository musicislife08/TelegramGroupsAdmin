using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for managing Web Push (in-app) notifications
/// Stores notifications to database and will trigger browser push in future
/// </summary>
public interface IWebPushNotificationService
{
    /// <summary>
    /// Send an in-app notification to a user
    /// </summary>
    Task<bool> SendAsync(
        string userId,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default);

    /// <summary>
    /// Get recent notifications for a user
    /// </summary>
    Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Get unread notification count for a user
    /// </summary>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    Task MarkAsReadAsync(long notificationId, CancellationToken ct = default);

    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task MarkAllAsReadAsync(string userId, CancellationToken ct = default);
}

public class WebPushNotificationService(
    IWebNotificationRepository repository,
    ILogger<WebPushNotificationService> logger) : IWebPushNotificationService
{
    public async Task<bool> SendAsync(
        string userId,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken ct = default)
    {
        try
        {
            var notification = new WebNotification
            {
                UserId = userId,
                EventType = eventType,
                Subject = subject,
                Message = message,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await repository.CreateAsync(notification, ct);

            logger.LogDebug("Created web notification for user {UserId}, event {EventType}",
                userId, eventType);

            // Future: Trigger browser push notification via Service Worker + VAPID
            // This will be implemented in Commit 3

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create web notification for user {UserId}", userId);
            return false;
        }
    }

    public Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default)
    {
        return repository.GetRecentAsync(userId, limit, offset, ct);
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken ct = default)
    {
        return repository.GetUnreadCountAsync(userId, ct);
    }

    public Task MarkAsReadAsync(long notificationId, CancellationToken ct = default)
    {
        return repository.MarkAsReadAsync(notificationId, ct);
    }

    public Task MarkAllAsReadAsync(string userId, CancellationToken ct = default)
    {
        return repository.MarkAllAsReadAsync(userId, ct);
    }
}
