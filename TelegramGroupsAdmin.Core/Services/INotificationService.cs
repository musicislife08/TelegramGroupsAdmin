using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.Services;

/// <summary>
/// Service for sending notifications to users through configured channels (Telegram DM, Email)
/// Three-tier routing: chat-scoped moderation, global moderation, and system-only
/// Respects per-user notification preferences and event filters
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Send a moderation notification to admins. Always includes global admins and owners.
    /// When chat is provided, also includes chat-specific admins.
    /// Used for: SpamDetected, SpamAutoDeleted, UserBanned, MessageReported, MalwareDetected, ExamFailed
    /// </summary>
    /// <param name="chat">Optional chat where the event occurred. When null, sends to global admins + owners only.</param>
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
        ChatIdentity? chat,
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
