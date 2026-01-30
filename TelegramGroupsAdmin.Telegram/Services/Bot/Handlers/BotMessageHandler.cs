using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram message operations.
/// Thin wrapper around ITelegramBotClient - no business logic, just API calls.
/// This is the ONLY layer that should touch ITelegramBotClientFactory for message operations.
/// </summary>
public class BotMessageHandler(ITelegramBotClientFactory botClientFactory) : IBotMessageHandler
{
    private readonly ITelegramBotClientFactory _botClientFactory = botClientFactory;

    public async Task<Message> SendAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.SendMessage(
            chatId,
            text,
            parseMode: parseMode ?? default,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.SendPhoto(
            chatId,
            photo,
            caption: caption,
            parseMode: parseMode ?? default,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.SendVideo(
            chatId,
            video,
            caption: caption,
            parseMode: parseMode ?? default,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task<Message> SendAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.SendAnimation(
            chatId,
            animation,
            caption: caption,
            parseMode: parseMode ?? default,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task<Message> EditTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.EditMessageText(
            chatId,
            messageId,
            text,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task<Message> EditCaptionAsync(
        long chatId,
        int messageId,
        string? caption,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        return await client.EditMessageCaption(
            chatId,
            messageId,
            caption ?? string.Empty,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: ct);
    }

    public async Task DeleteAsync(long chatId, int messageId, CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        await client.DeleteMessage(chatId, messageId, ct);
    }

    public async Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        var client = await _botClientFactory.GetBotClientAsync();
        await client.AnswerCallbackQuery(callbackQueryId, text: text, showAlert: showAlert, cancellationToken: ct);
    }
}
