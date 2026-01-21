using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for sending notifications to users through configured channels (Telegram DM, Email)
/// Hybrid approach: chat-specific events notify chat admins, system events notify Owners only
/// Respects per-user notification preferences and event filters
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a chat-specific notification to all admins who manage the specified chat
    /// Used for: SpamDetected, SpamAutoDeleted, UserBanned, MessageReported, MalwareDetected, ExamFailed
    /// </summary>
    /// <param name="chatId">Chat ID where the event occurred</param>
    /// <param name="eventType">Type of event triggering the notification</param>
    /// <param name="subject">Notification subject/title</param>
    /// <param name="message">Notification message body</param>
    /// <param name="reportId">Optional report ID for moderation action buttons</param>
    /// <param name="photoPath">Optional absolute path to photo for DM with image</param>
    /// <param name="reportedUserId">Optional reported user's Telegram ID for moderation actions</param>
    /// <param name="reportType">Optional report type for building correct action buttons</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping userId to delivery success</returns>
    Task<Dictionary<string, bool>> SendChatNotificationAsync(
        long chatId,
        NotificationEventType eventType,
        string subject,
        string message,
        long? reportId = null,
        string? photoPath = null,
        long? reportedUserId = null,
        ReportType? reportType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a system-level notification to all Owner users only
    /// Used for infrastructure events: BackupFailed, ChatHealthWarning
    /// </summary>
    /// <param name="eventType">Type of event triggering the notification</param>
    /// <param name="subject">Notification subject/title</param>
    /// <param name="message">Notification message body</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping userId to delivery success</returns>
    Task<Dictionary<string, bool>> SendSystemNotificationAsync(
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a notification to a specific user (for special cases)
    /// Automatically routes to enabled channels based on user preferences
    /// </summary>
    /// <param name="user">Web user record</param>
    /// <param name="eventType">Type of event triggering the notification</param>
    /// <param name="subject">Notification subject/title</param>
    /// <param name="message">Notification message body</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if at least one channel delivered successfully</returns>
    Task<bool> SendNotificationAsync(
        UserRecord user,
        NotificationEventType eventType,
        string subject,
        string message,
        CancellationToken cancellationToken = default);
}
