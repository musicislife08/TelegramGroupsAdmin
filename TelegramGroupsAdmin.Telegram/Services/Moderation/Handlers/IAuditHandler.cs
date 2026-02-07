using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Handlers;

/// <summary>
/// Handler for audit logging (user_actions table).
/// Called directly by orchestrator after successful actions.
/// NOTE: LogWarnAsync only creates audit trail - warning records are inserted by WarnHandler.
/// </summary>
public interface IAuditHandler
{
    Task LogBanAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogTempBanAsync(UserIdentity user, Actor executor, TimeSpan duration, string? reason, CancellationToken cancellationToken = default);

    Task LogUnbanAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log warning to audit trail. Does NOT insert warning record (WarnHandler does that).
    /// </summary>
    Task LogWarnAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogTrustAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default);

    Task LogUntrustAsync(UserIdentity user, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log message deletion to audit trail.
    /// </summary>
    Task LogDeleteAsync(long messageId, ChatIdentity chat, UserIdentity user, Actor executor, CancellationToken cancellationToken = default);

    Task LogRestrictAsync(UserIdentity user, ChatIdentity? chat, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log permission restoration to audit trail.
    /// Called when user permissions are restored to chat defaults (e.g., exam pass, welcome accept).
    /// </summary>
    Task LogRestorePermissionsAsync(UserIdentity user, ChatIdentity chat, Actor executor, string? reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log kick to audit trail.
    /// Called when user is kicked from a specific chat (ban then immediate unban).
    /// </summary>
    Task LogKickAsync(UserIdentity user, ChatIdentity chat, Actor executor, string? reason, CancellationToken cancellationToken = default);
}
