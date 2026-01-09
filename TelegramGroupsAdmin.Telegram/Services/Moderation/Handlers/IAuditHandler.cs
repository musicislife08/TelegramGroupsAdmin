using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Handler for audit logging (user_actions table).
/// Called directly by orchestrator after successful actions.
/// NOTE: LogWarnAsync only creates audit trail - warning records are inserted by WarnHandler.
/// </summary>
public interface IAuditHandler
{
    Task LogBanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogTempBanAsync(long userId, Actor executor, TimeSpan duration, string? reason, CancellationToken cancellationToken = default);

    Task LogUnbanAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log warning to audit trail. Does NOT insert warning record (WarnHandler does that).
    /// </summary>
    Task LogWarnAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogTrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogUntrustAsync(long userId, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log message deletion to audit trail.
    /// userId is required for FK constraint to telegram_users.
    /// </summary>
    // NOTE: Parameter naming inconsistency - see issue #156 (standardize 'ct' → 'cancellationToken')
    Task LogDeleteAsync(long messageId, long chatId, long userId, Actor executor, CancellationToken cancellationToken = default);

    Task LogRestrictAsync(long userId, long chatId, Actor executor, string? reason, CancellationToken cancellationToken = default);
}
