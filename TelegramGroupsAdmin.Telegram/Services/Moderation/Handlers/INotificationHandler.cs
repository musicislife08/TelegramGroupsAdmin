using TelegramGroupsAdmin.Core.Models;
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
}
