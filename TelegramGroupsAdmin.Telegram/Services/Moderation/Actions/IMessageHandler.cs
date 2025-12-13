using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Actions;

/// <summary>
/// Domain handler for message operations.
/// Owns backfill (ensuring messages exist in DB) and deletion.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Ensure message exists in database, backfilling from Telegram if needed.
    /// Call this before operations that require the message to exist (e.g., training data creation).
    /// </summary>
    /// <param name="messageId">The message ID to ensure exists.</param>
    /// <param name="chatId">The chat the message belongs to.</param>
    /// <param name="telegramMessage">Optional Telegram message object for backfill if not in DB.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result indicating whether message exists (either already or after backfill).</returns>
    Task<BackfillResult> EnsureExistsAsync(
        long messageId,
        long chatId,
        Message? telegramMessage = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete message from Telegram and mark as deleted in database.
    /// </summary>
    Task<DeleteResult> DeleteAsync(
        long chatId,
        long messageId,
        Actor executor,
        CancellationToken ct = default);
}
