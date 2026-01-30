using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services.Moderation.Actions.Results;
using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for moderation message operations.
/// Owns backfill (ensuring messages exist in DB) and deletion.
/// Named IBotModerationMessageHandler to avoid conflict with IBotMessageHandler (send/edit).
/// This is the ONLY layer that should touch ITelegramBotClientFactory for moderation message operations.
/// </summary>
public interface IBotModerationMessageHandler
{
    /// <summary>
    /// Ensure message exists in database, backfilling from Telegram if needed.
    /// Call this before operations that require the message to exist (e.g., training data creation).
    /// </summary>
    /// <param name="messageId">The message ID to ensure exists.</param>
    /// <param name="chatId">The chat the message belongs to.</param>
    /// <param name="telegramMessage">Optional Telegram message object for backfill if not in DB.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating whether message exists (either already or after backfill).</returns>
    Task<BackfillResult> EnsureExistsAsync(
        long messageId,
        long chatId,
        Message? telegramMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete message from Telegram and mark as deleted in database.
    /// </summary>
    Task<DeleteResult> DeleteAsync(
        long chatId,
        long messageId,
        Actor executor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule a background job to delete all messages from a user across all managed chats.
    /// Used for cleanup after bans.
    /// </summary>
    /// <param name="userId">The Telegram user ID whose messages should be deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ScheduleUserMessagesCleanupAsync(
        long userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get enriched message with detection history, translation, and media paths.
    /// Used by orchestrator to build rich notifications after spam ban.
    /// </summary>
    /// <param name="messageId">The message ID to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enriched message with detection history, or null if not found.</returns>
    Task<MessageWithDetectionHistory?> GetEnrichedAsync(
        long messageId,
        CancellationToken cancellationToken = default);
}
