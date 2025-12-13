using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for warning operations.
/// Writes to warnings table (source of truth for active warning counts).
/// Does NOT know about bans, trust, or notifications (orchestrator composes those).
/// </summary>
public interface IWarnHandler
{
    /// <summary>
    /// Issue a warning to user. Returns the post-insert active warning count.
    /// </summary>
    Task<WarnResult> WarnAsync(
        long userId,
        Actor executor,
        string? reason,
        long? chatId = null,
        long? messageId = null,
        CancellationToken ct = default);
}
