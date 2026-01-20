using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for restriction/mute operations.
/// Used by welcome flow and future restriction features.
/// Supports both single-chat and global restrictions.
/// </summary>
public interface IRestrictHandler
{
    /// <summary>
    /// Restrict user (mute) in a specific chat or globally.
    /// </summary>
    /// <param name="userId">Telegram user ID to restrict.</param>
    /// <param name="chatId">Target chat ID. Use 0 for global restriction across all managed chats.</param>
    /// <param name="executor">Who initiated the restriction.</param>
    /// <param name="duration">How long the restriction lasts.</param>
    /// <param name="reason">Reason for the restriction (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with success status and affected chat count.</returns>
    Task<RestrictResult> RestrictAsync(
        long userId,
        long chatId,
        Actor executor,
        TimeSpan duration,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore user permissions to the chat's default permissions (unrestrict/unmute).
    /// Used when approving users through welcome/exam flows.
    /// </summary>
    /// <param name="userId">Telegram user ID to restore.</param>
    /// <param name="chatId">Target chat ID.</param>
    /// <param name="executor">Who initiated the restoration.</param>
    /// <param name="reason">Reason for restoring permissions (for logging).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with success status.</returns>
    Task<RestrictResult> RestorePermissionsAsync(
        long userId,
        long chatId,
        Actor executor,
        string? reason,
        CancellationToken cancellationToken = default);
}
