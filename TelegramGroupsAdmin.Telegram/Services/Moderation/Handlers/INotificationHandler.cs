using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Handler for DM notifications and admin alerts.
/// Called directly by orchestrator after successful actions.
/// Returns results so orchestrator can decide how to handle failures.
/// </summary>
public interface INotificationHandler
{
    /// <summary>
    /// Notify user about a warning they received.
    /// </summary>
    Task<NotificationResult> NotifyUserWarningAsync(
        long userId,
        int warningCount,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify user about a temporary ban with expected unban time.
    /// </summary>
    Task<NotificationResult> NotifyUserTempBanAsync(
        long userId,
        TimeSpan duration,
        DateTimeOffset expiresAt,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify admins about a ban action (for audit/awareness).
    /// </summary>
    Task<NotificationResult> NotifyAdminsBanAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify admins about spam ban with full message context.
    /// Rich notification with message preview, detection details, and media.
    /// </summary>
    /// <param name="enrichedMessage">Message with detection history, translation, media paths.</param>
    /// <param name="chatsAffected">Number of chats the user was banned from.</param>
    /// <param name="messageDeleted">Whether the spam message was deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NotificationResult> NotifyAdminsSpamBanAsync(
        MessageWithDetectionHistory enrichedMessage,
        int chatsAffected,
        bool messageDeleted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify user about a critical check violation (trusted users get explanation, not ban).
    /// Critical checks (URL filtering, file scanning) bypass trust status.
    /// </summary>
    /// <param name="userId">The Telegram user ID to notify.</param>
    /// <param name="violations">List of violations that caused the message deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<NotificationResult> NotifyUserCriticalViolationAsync(
        long userId,
        List<string> violations,
        CancellationToken cancellationToken = default);
}
