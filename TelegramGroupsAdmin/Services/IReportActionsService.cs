namespace TelegramGroupsAdmin.Services;

/// <summary>
/// Service for handling admin actions on reports (spam/ban/warn/dismiss)
/// </summary>
public interface IReportActionsService
{
    /// <summary>
    /// Mark report as spam, delete message, and notify reporter
    /// </summary>
    Task HandleSpamActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban user across all managed chats and notify reporter
    /// </summary>
    Task HandleBanActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Warn user and notify reporter
    /// </summary>
    Task HandleWarnActionAsync(long reportId, Actor executor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismiss report and notify reporter
    /// </summary>
    Task HandleDismissActionAsync(long reportId, Actor executor, string? reason = null, CancellationToken cancellationToken = default);
}
