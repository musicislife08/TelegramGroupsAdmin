using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Blazor state service for managing in-app notification UI state
/// Scoped per circuit - tracks unread count and recent notifications
/// </summary>
public class NotificationStateService : IDisposable
{
    /// <summary>
    /// Number of recent notifications to fetch from database
    /// </summary>
    private const int DefaultNotificationLimit = 20;

    /// <summary>
    /// Maximum notifications to keep in memory per circuit
    /// </summary>
    private const int MaxNotificationsInMemory = 50;

    private readonly IWebPushNotificationService _notificationService;
    private readonly ILogger<NotificationStateService> _logger;
    private string? _userId;
    private int _unreadCount;
    private List<WebNotification> _notifications = [];
    private bool _isLoaded;

    public event Func<Task>? OnChange;

    public int UnreadCount => _unreadCount;
    public IReadOnlyList<WebNotification> Notifications => _notifications;
    public bool IsLoaded => _isLoaded;

    public NotificationStateService(
        IWebPushNotificationService notificationService,
        ILogger<NotificationStateService> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Initialize state for a user (call from layout OnInitializedAsync)
    /// </summary>
    public async Task InitializeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (_userId == userId && _isLoaded)
            return; // Already initialized for this user

        _userId = userId;
        await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Refresh notifications from database
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId))
            return;

        try
        {
            _unreadCount = await _notificationService.GetUnreadCountAsync(_userId, cancellationToken);
            _notifications = (await _notificationService.GetRecentAsync(_userId, DefaultNotificationLimit, 0, cancellationToken)).ToList();
            _isLoaded = true;

            await NotifyStateChangedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh notifications for user {UserId}", _userId);
        }
    }

    /// <summary>
    /// Mark a single notification as read
    /// </summary>
    public async Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        await _notificationService.MarkAsReadAsync(notificationId, cancellationToken);

        // Update local state
        var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
        if (notification != null && !notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
            _unreadCount = Math.Max(0, _unreadCount - 1);
            await NotifyStateChangedAsync();
        }
    }

    /// <summary>
    /// Mark all notifications as read
    /// </summary>
    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId))
            return;

        await _notificationService.MarkAllAsReadAsync(_userId, cancellationToken);

        // Update local state
        foreach (var notification in _notifications.Where(n => !n.IsRead))
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
        }
        _unreadCount = 0;

        await NotifyStateChangedAsync();
    }

    /// <summary>
    /// Add a new notification (called when receiving real-time updates)
    /// </summary>
    public async Task AddNotificationAsync(WebNotification notification)
    {
        _notifications.Insert(0, notification);
        if (!notification.IsRead)
        {
            _unreadCount++;
        }

        // Keep list size reasonable - remove from end (O(1) for List<T>)
        while (_notifications.Count > MaxNotificationsInMemory)
        {
            _notifications.RemoveAt(_notifications.Count - 1);
        }

        await NotifyStateChangedAsync();
    }

    /// <summary>
    /// Delete a single notification
    /// </summary>
    public async Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        await _notificationService.DeleteAsync(notificationId, cancellationToken);

        // Update local state
        var notification = _notifications.FirstOrDefault(n => n.Id == notificationId);
        if (notification != null)
        {
            if (!notification.IsRead)
            {
                _unreadCount = Math.Max(0, _unreadCount - 1);
            }
            _notifications.Remove(notification);
            await NotifyStateChangedAsync();
        }
    }

    /// <summary>
    /// Delete all notifications for current user
    /// </summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_userId))
            return;

        await _notificationService.DeleteAllAsync(_userId, cancellationToken);

        // Update local state
        _notifications.Clear();
        _unreadCount = 0;

        await NotifyStateChangedAsync();
    }

    private async Task NotifyStateChangedAsync()
    {
        if (OnChange != null)
        {
            await OnChange.Invoke();
        }
    }

    public void Dispose()
    {
        OnChange = null;
    }
}
