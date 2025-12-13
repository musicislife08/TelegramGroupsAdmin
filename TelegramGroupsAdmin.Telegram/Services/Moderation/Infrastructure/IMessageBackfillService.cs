using Telegram.Bot.Types;

namespace TelegramGroupsAdmin.Telegram.Services.Moderation.Infrastructure;

/// <summary>
/// Service for ensuring messages exist in the database before creating training data.
/// </summary>
public interface IMessageBackfillService
{
    /// <summary>
    /// Backfill a message to the database if it doesn't already exist.
    /// Used when processing spam reports for messages the bot didn't see in real-time.
    /// </summary>
    /// <param name="messageId">The Telegram message ID.</param>
    /// <param name="chatId">The chat ID where the message was sent.</param>
    /// <param name="telegramMessage">The Telegram message object with full data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if message was backfilled, false if it already existed or has no text content.</returns>
    Task<bool> BackfillIfMissingAsync(
        long messageId,
        long chatId,
        Message telegramMessage,
        CancellationToken ct = default);
}
