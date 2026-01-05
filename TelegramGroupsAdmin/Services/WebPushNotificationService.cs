using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Repositories;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

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

public class WebPushNotificationService : IWebPushNotificationService
{
    private readonly IWebNotificationRepository _notificationRepository;
    private readonly IPushSubscriptionsRepository _subscriptionRepository;
    private readonly ISystemConfigRepository _configRepository;
    private readonly IUserRepository _userRepository;
    private readonly PushServiceClient _pushServiceClient;
    private readonly ILogger<WebPushNotificationService> _logger;

    // Cache VAPID auth to avoid re-creating on every push
    private VapidAuthentication? _vapidAuth;
    private string? _cachedPublicKey;

    public WebPushNotificationService(
        IWebNotificationRepository notificationRepository,
        IPushSubscriptionsRepository subscriptionRepository,
        ISystemConfigRepository configRepository,
        IUserRepository userRepository,
        PushServiceClient pushServiceClient,
        ILogger<WebPushNotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _subscriptionRepository = subscriptionRepository;
        _configRepository = configRepository;
        _userRepository = userRepository;
        _pushServiceClient = pushServiceClient;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        UserRecord user,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Always save to database (for in-app bell)
            var notification = new WebNotification
            {
                UserId = user.Id,
                EventType = eventType,
                Subject = subject,
                Message = message,
                IsRead = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await _notificationRepository.CreateAsync(notification, cancellationToken);

            _logger.LogDebug("Created web notification for {User}, event {EventType}",
                user.ToLogDebug(), eventType);

            // 2. Send browser push to all user's subscribed devices (if VAPID configured)
            await SendBrowserPushAsync(user, subject, message, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create web notification for {User}", user.ToLogDebug());
            return false;
        }
    }

    private async Task SendBrowserPushAsync(
        UserRecord user,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        try
        {
            // Load VAPID keys from database (cached after first load)
            var vapidAuth = await GetOrCreateVapidAuthAsync(cancellationToken);
            if (vapidAuth == null)
            {
                _logger.LogDebug("VAPID not configured, skipping browser push");
                return;
            }

            var subscriptions = await _subscriptionRepository.GetByUserIdAsync(user.Id, cancellationToken);

            if (!subscriptions.Any())
            {
                _logger.LogDebug("{User} has no push subscriptions", user.ToLogDebug());
                return;
            }

            var payload = JsonSerializer.Serialize(new
            {
                title,
                body,
                icon = "/icon-192.png",
                tag = $"tga-{DateTime.UtcNow.Ticks}",
                url = "/"
            });

            var message = new PushMessage(payload)
            {
                Urgency = PushMessageUrgency.Normal
            };

            foreach (var subscription in subscriptions)
            {
                try
                {
                    var pushSubscription = new WebPushSubscription
                    {
                        Endpoint = subscription.Endpoint,
                        Keys = new Dictionary<string, string>
                        {
                            ["p256dh"] = subscription.P256dh,
                            ["auth"] = subscription.Auth
                        }
                    };

                    _pushServiceClient.DefaultAuthentication = vapidAuth;
                    await _pushServiceClient.RequestPushMessageDeliveryAsync(
                        pushSubscription,
                        message,
                        cancellationToken);

                    _logger.LogDebug("Sent browser push to {User}", user.ToLogDebug());
                }
                catch (PushServiceClientException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
                {
                    // Subscription expired or unsubscribed - remove it
                    _logger.LogInformation(
                        "Push subscription expired for {User}, removing endpoint",
                        user.ToLogInfo());
                    await _subscriptionRepository.DeleteByEndpointAsync(subscription.Endpoint, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to send browser push to {User}",
                        user.ToLogDebug());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send browser push notifications to {User}", user.ToLogDebug());
        }
    }

    private async Task<VapidAuthentication?> GetOrCreateVapidAuthAsync(CancellationToken cancellationToken)
    {
        if (_vapidAuth != null)
            return _vapidAuth;

        try
        {
            // Check if WebPush is enabled
            var webPushConfig = await _configRepository.GetWebPushConfigAsync(cancellationToken);
            if (webPushConfig?.Enabled != true)
            {
                _logger.LogDebug("Web Push is disabled in config");
                return null;
            }

            // Check if VAPID keys exist
            if (!await _configRepository.HasVapidKeysAsync(cancellationToken))
            {
                _logger.LogDebug("VAPID keys not configured, Web Push disabled");
                return null;
            }

            var publicKey = webPushConfig.VapidPublicKey;
            var privateKey = await _configRepository.GetVapidPrivateKeyAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            {
                _logger.LogDebug("VAPID keys incomplete, Web Push disabled");
                return null;
            }

            // Determine VAPID subject email (contact for push service)
            // Priority: 1. WebPushConfig.ContactEmail, 2. Primary Owner's email
            var contactEmail = webPushConfig.ContactEmail;
            if (string.IsNullOrEmpty(contactEmail))
            {
                contactEmail = await _userRepository.GetPrimaryOwnerEmailAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(contactEmail))
            {
                _logger.LogWarning("No contact email configured for VAPID - Web Push disabled. " +
                    "Configure in Settings → Notifications → Web Push or ensure an active Owner account exists.");
                return null;
            }

            _vapidAuth = new VapidAuthentication(publicKey, privateKey)
            {
                Subject = $"mailto:{contactEmail}"
            };

            _cachedPublicKey = publicKey;
            _logger.LogInformation("VAPID authentication configured with contact: {Email}", contactEmail);

            return _vapidAuth;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VAPID configuration");
            return null;
        }
    }

    public async Task<string?> GetVapidPublicKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedPublicKey != null)
        {
            _logger.LogDebug("Returning cached VAPID public key (length: {Length})", _cachedPublicKey.Length);
            return _cachedPublicKey;
        }

        try
        {
            var webPushConfig = await _configRepository.GetWebPushConfigAsync(cancellationToken);
            _cachedPublicKey = webPushConfig?.VapidPublicKey;
            _logger.LogDebug("Loaded VAPID public key (length: {Length})", _cachedPublicKey?.Length ?? 0);
            return _cachedPublicKey;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load VAPID public key");
            return null;
        }
    }

    public Task<IReadOnlyList<WebNotification>> GetRecentAsync(
        string userId,
        int limit = NotificationConstants.DefaultNotificationLimit,
        int offset = NotificationConstants.DefaultNotificationOffset,
        CancellationToken cancellationToken = default)
    {
        return _notificationRepository.GetRecentAsync(userId, limit, offset, cancellationToken);
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.GetUnreadCountAsync(userId, cancellationToken);
    }

    public Task MarkAsReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.MarkAsReadAsync(notificationId, cancellationToken);
    }

    public Task MarkAllAsReadAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.MarkAllAsReadAsync(userId, cancellationToken);
    }

    public Task<int> DeleteOldReadNotificationsAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.DeleteOldReadNotificationsAsync(olderThan, cancellationToken);
    }

    public Task DeleteAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.DeleteAsync(notificationId, cancellationToken);
    }

    public Task DeleteAllAsync(string userId, CancellationToken cancellationToken = default)
    {
        return _notificationRepository.DeleteAllAsync(userId, cancellationToken);
    }
}
