using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram message operations.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for message operations.
/// Services should use IBotMessageService which orchestrates this handler.
/// </summary>
public interface IBotMessageHandler
{
    /// <summary>Send a text message.</summary>
    Task<Message> SendAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send a photo with optional caption.</summary>
    Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send a video with optional caption.</summary>
    Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send an animation (GIF) with optional caption.</summary>
    Task<Message> SendAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Edit a text message.</summary>
    Task<Message> EditTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Edit a message caption.</summary>
    Task<Message> EditCaptionAsync(
        long chatId,
        int messageId,
        string? caption,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Delete a message.</summary>
    Task DeleteAsync(long chatId, int messageId, CancellationToken ct = default);

    /// <summary>Answer a callback query (acknowledge button click).</summary>
    Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default);
}
