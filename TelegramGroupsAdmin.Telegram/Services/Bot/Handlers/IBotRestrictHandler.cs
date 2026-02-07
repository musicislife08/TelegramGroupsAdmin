using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for restriction/mute operations.
/// Used by welcome flow and future restriction features.
/// Supports both single-chat (chat provided) and global (chat null) restrictions.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for restrict operations.
/// </summary>
public interface IBotRestrictHandler
{
    /// <summary>
    /// Restrict user (mute) in a specific chat or globally.
    /// </summary>
    /// <param name="user">Identity of the user to restrict.</param>
    /// <param name="chat">Target chat. Null for global restriction across all managed chats.</param>
    /// <param name="executor">Who initiated the restriction.</param>
    /// <param name="duration">How long the restriction lasts.</param>
    /// <param name="reason">Reason for the restriction (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with success status and affected chat count.</returns>
    Task<RestrictResult> RestrictAsync(
        UserIdentity user,
        ChatIdentity? chat,
        Actor executor,
        TimeSpan duration,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore user permissions to the chat's default permissions (unrestrict/unmute).
    /// Used when approving users through welcome/exam flows.
    /// </summary>
    /// <param name="user">Identity of the user to restore.</param>
    /// <param name="chat">Target chat.</param>
    /// <param name="executor">Who initiated the restoration.</param>
    /// <param name="reason">Reason for restoring permissions (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with success status.</returns>
    Task<RestrictResult> RestorePermissionsAsync(
        UserIdentity user,
        ChatIdentity chat,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
