using System.Collections.Concurrent;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Services.Media;

/// <summary>
/// Singleton event bus for media download notifications
/// Thread-safe subscription management with broadcast notifications
/// Multiple components can subscribe to the same media
/// </summary>
public class MediaNotificationService : IMediaNotificationService
{
    private readonly ConcurrentDictionary<string, List<Action>> _subscriptions = new();
    private readonly object _lock = new();

    public void Subscribe(long messageId, MediaType mediaType, Action callback)
    {
        var key = $"{messageId}:{mediaType}";
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(key, out var callbacks))
            {
                callbacks = new List<Action>();
                _subscriptions[key] = callbacks;
            }
            callbacks.Add(callback);
        }
    }

    public void SubscribeUserPhoto(long userId, Action callback)
    {
        var key = $"{userId}:photo";
        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(key, out var callbacks))
            {
                callbacks = new List<Action>();
                _subscriptions[key] = callbacks;
            }
            callbacks.Add(callback);
        }
    }

    public void Unsubscribe(long messageId, MediaType mediaType, Action callback)
    {
        var key = $"{messageId}:{mediaType}";
        lock (_lock)
        {
            if (_subscriptions.TryGetValue(key, out var callbacks))
            {
                callbacks.Remove(callback);
                if (callbacks.Count == 0)
                {
                    _subscriptions.TryRemove(key, out _);
                }
            }
        }
    }

    public void UnsubscribeUserPhoto(long userId, Action callback)
    {
        var key = $"{userId}:photo";
        lock (_lock)
        {
            if (_subscriptions.TryGetValue(key, out var callbacks))
            {
                callbacks.Remove(callback);
                if (callbacks.Count == 0)
                {
                    _subscriptions.TryRemove(key, out _);
                }
            }
        }
    }

    public void NotifyMediaReady(long messageId, MediaType mediaType)
    {
        var key = $"{messageId}:{mediaType}";
        List<Action>? callbacks;

        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(key, out callbacks) || callbacks == null)
            {
                return; // No subscribers
            }
            // Remove from subscriptions after notification
            _subscriptions.TryRemove(key, out _);
        }

        // Fire all callbacks (multiple UI components may be subscribed)
        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception)
            {
                // Ignore callback errors to prevent one component from breaking others
            }
        }
    }

    public void NotifyUserPhotoReady(long userId)
    {
        var key = $"{userId}:photo";
        List<Action>? callbacks;

        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(key, out callbacks) || callbacks == null)
            {
                return; // No subscribers
            }
            // Remove from subscriptions after notification
            _subscriptions.TryRemove(key, out _);
        }

        // Fire all callbacks
        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception)
            {
                // Ignore callback errors
            }
        }
    }
}
