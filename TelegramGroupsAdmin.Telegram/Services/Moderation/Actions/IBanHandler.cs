using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for ban operations (ban, temp-ban, unban).
/// Handles Telegram API calls and database updates.
/// Does NOT know about trust, warnings, or notifications (orchestrator composes those).
/// </summary>
public interface IBanHandler
{
    /// <summary>
    /// Ban user globally across all managed chats.
    /// </summary>
    Task<BanResult> BanAsync(
        long userId,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban user in a single specific chat (lazy sync for chats added after global ban).
    /// </summary>
    Task<BanResult> BanAsync(
        User user,
        Chat chat,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily ban user globally with automatic unban after duration.
    /// </summary>
    Task<TempBanResult> TempBanAsync(
        long userId,
        Actor executor,
        TimeSpan duration,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unban user globally across all managed chats.
    /// </summary>
    Task<UnbanResult> UnbanAsync(
        long userId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
