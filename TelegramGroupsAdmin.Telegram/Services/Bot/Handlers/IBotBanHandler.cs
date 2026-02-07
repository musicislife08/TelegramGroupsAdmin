using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for ban operations (ban, temp-ban, unban, kick).
/// Handles Telegram API calls and database updates.
/// Does NOT know about trust, warnings, or notifications (orchestrator composes those).
/// This is the ONLY layer that should touch ITelegramBotClientFactory for ban operations.
/// </summary>
public interface IBotBanHandler
{
    /// <summary>
    /// Ban user globally across all managed chats.
    /// </summary>
    Task<BanResult> BanAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ban user in a single specific chat (lazy sync for chats added after global ban).
    /// Also used by BotProtectionService to ban unauthorized bots in a single chat.
    /// </summary>
    Task<BanResult> BanInChatAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Temporarily ban user globally with automatic unban after duration.
    /// </summary>
    Task<TempBanResult> TempBanAsync(
        UserIdentity user,
        Actor executor,
        TimeSpan duration,
        string? reason,
        long? triggeredByMessageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unban user globally across all managed chats.
    /// </summary>
    Task<UnbanResult> UnbanAsync(
        UserIdentity user,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kick user from a specific chat (ban then immediately unban).
    /// Does not affect other chats or create permanent ban record.
    /// Used for welcome flow denials and exam failures.
    /// </summary>
    Task<BanResult> KickFromChatAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
