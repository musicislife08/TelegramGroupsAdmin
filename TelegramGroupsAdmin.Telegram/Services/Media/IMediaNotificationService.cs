using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Media;

/// <summary>
/// Event bus for notifying UI components when media downloads complete
/// Singleton service that manages subscriptions and fires notifications
/// </summary>
public interface IMediaNotificationService
{
    /// <summary>
    /// Subscribe to notifications for a specific media file
    /// </summary>
    void Subscribe(long messageId, MediaType mediaType, Action callback);

    /// <summary>
    /// Subscribe to notifications for a specific user photo
    /// </summary>
    void SubscribeUserPhoto(long userId, Action callback);

    /// <summary>
    /// Unsubscribe from media notifications
    /// </summary>
    void Unsubscribe(long messageId, MediaType mediaType, Action callback);

    /// <summary>
    /// Unsubscribe from user photo notifications
    /// </summary>
    void UnsubscribeUserPhoto(long userId, Action callback);

    /// <summary>
    /// Notify all subscribers that media is ready
    /// </summary>
    void NotifyMediaReady(long messageId, MediaType mediaType);

    /// <summary>
    /// Notify all subscribers that user photo is ready
    /// </summary>
    void NotifyUserPhotoReady(long userId);
}
