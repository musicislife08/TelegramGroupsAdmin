namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Manages notification state for the current user's session.
/// Scoped service that maintains in-memory notification cache.
/// </summary>
public interface INotificationStateService : IDisposable
{
    /// <summary>Raised when notification state changes (new notification, read, deleted).</summary>
    event Func<Task>? OnChange;

    /// <summary>Gets the count of unread notifications.</summary>
    int UnreadCount { get; }

    /// <summary>Gets the cached list of notifications.</summary>
    IReadOnlyList<WebNotification> Notifications { get; }

    /// <summary>Indicates whether notifications have been loaded from database.</summary>
    bool IsLoaded { get; }

    /// <summary>Initializes the notification state for a user, loading from database.</summary>
    Task InitializeAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Refreshes notifications from the database.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks a specific notification as read.</summary>
    Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marks all notifications as read.</summary>
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a notification to the in-memory cache (for real-time updates).</summary>
    Task AddNotificationAsync(WebNotification notification);

    /// <summary>Deletes a specific notification.</summary>
    Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default);

    /// <summary>Deletes all notifications for the current user.</summary>
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
