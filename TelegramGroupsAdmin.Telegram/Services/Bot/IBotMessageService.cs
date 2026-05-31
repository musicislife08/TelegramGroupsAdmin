using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramGroupsAdmin.Core.Utilities;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Centralized service for sending bot messages AND saving them to the messages table.
/// Ensures all bot-sent messages are tracked in the database for complete conversation history.
/// </summary>
public interface IBotMessageService
{
    /// <summary>
    /// Send message via bot AND save to messages table.
    /// Returns the sent Message object (contains MessageId for tracking).
    /// </summary>
    Task<Message> SendAndSaveMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit message via bot AND save edit history to message_edits table.
    /// Used for web UI editing of bot messages.
    /// </summary>
    Task<Message> EditAndUpdateMessageAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit message via bot AND save edit history to message_edits table.
    /// Entity-based overload — sends text + entities with no parse_mode.
    /// </summary>
    Task<Message> EditAndUpdateMessageAsync(
        long chatId,
        int messageId,
        TelegramMessage message,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete message via bot AND mark as deleted in database.
    /// Gracefully handles cases where message is already deleted from Telegram.
    /// </summary>
    Task DeleteAndMarkMessageAsync(
        long chatId,
        int messageId,
        string deletionSource = "bot_cleanup",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a bot message to the database without sending it.
    /// Used when the message was already sent and then edited (e.g., verifying → welcome message).
    /// </summary>
    Task SaveBotMessageAsync(
        long chatId,
        int messageId,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answer a callback query to acknowledge button click and remove loading state.
    /// </summary>
    Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send message via bot AND save to messages table.
    /// Entity-based overload — sends text + entities with no parse_mode.
    /// </summary>
    Task<Message> SendAndSaveMessageAsync(
        long chatId,
        TelegramMessage message,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an animation (GIF) to a chat AND save to message history.
    /// Used for ban celebrations and other GIF content that should appear in message history.
    /// </summary>
    Task<Message> SendAndSaveAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an animation (GIF) to a chat AND save to message history.
    /// Entity-based caption overload — degrades to plain text (SendAnimation has no caption_entities in this client surface).
    /// </summary>
    Task<Message> SendAndSaveAnimationAsync(
        long chatId,
        InputFile animation,
        TelegramMessage caption,
        CancellationToken cancellationToken = default);
}
