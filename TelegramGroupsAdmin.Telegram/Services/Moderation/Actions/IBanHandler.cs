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

    /// <summary>
    /// Kick user from a specific chat (ban then immediately unban).
    /// Does not affect other chats or create permanent ban record.
    /// Used for welcome flow denials and exam failures.
    /// </summary>
    /// <param name="userId">Telegram user ID to kick.</param>
    /// <param name="chatId">Chat to kick the user from.</param>
    /// <param name="executor">Who initiated the kick.</param>
    /// <param name="reason">Reason for the kick (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with success status.</returns>
    Task<BanResult> KickFromChatAsync(
        long userId,
        long chatId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
