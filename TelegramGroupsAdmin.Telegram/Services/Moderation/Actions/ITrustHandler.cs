using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for trust operations.
/// Updates telegram_users.is_trusted flag (the source of truth).
/// Does NOT know about bans, warnings, or notifications (orchestrator composes those).
/// </summary>
public interface ITrustHandler
{
    /// <summary>
    /// Trust user globally (bypass spam detection).
    /// Sets is_trusted = true.
    /// </summary>
    Task<TrustResult> TrustAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove trust from user globally.
    /// Sets is_trusted = false.
    /// </summary>
    Task<UntrustResult> UntrustAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
