using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories.Mappings;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing pending notifications (failed DM deliveries queued for retry)
/// </summary>
public interface IPendingNotificationsRepository
{
    /// <summary>
    /// Add a new pending notification to the queue
    /// </summary>
    Task<PendingNotificationModel> AddPendingNotificationAsync(
        long telegramUserId,
        string notificationType,
        string messageText,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all pending notifications for a specific user
    /// </summary>
    Task<List<PendingNotificationModel>> GetPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a pending notification after successful delivery
    /// </summary>
    Task DeletePendingNotificationAsync(
        long notificationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment the retry count for a pending notification
    /// </summary>
    Task IncrementRetryCountAsync(
        long notificationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all pending notifications for a specific user
    /// </summary>
    Task DeleteAllPendingNotificationsForUserAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all expired pending notifications (cleanup job)
    /// </summary>
    Task<int> DeleteExpiredNotificationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of pending notifications for a user
    /// </summary>
    Task<int> GetPendingNotificationCountAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);
}
