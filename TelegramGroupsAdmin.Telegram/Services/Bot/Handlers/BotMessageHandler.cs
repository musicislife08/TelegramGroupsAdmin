using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Bot.Handlers;

/// <summary>
/// Low-level handler for Telegram message operations.
/// Thin wrapper around ITelegramApiClient - no business logic, just API calls.
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
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.SendMessageAsync(
            chatId,
            text,
            parseMode: parseMode,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            ct: ct);
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
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.SendPhotoAsync(
            chatId,
            photo,
            caption: caption,
            parseMode: parseMode,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            ct: ct);
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
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.SendVideoAsync(
            chatId,
            video,
            caption: caption,
            parseMode: parseMode,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            ct: ct);
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
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.SendAnimationAsync(
            chatId,
            animation,
            caption: caption,
            parseMode: parseMode,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            ct: ct);
    }

    public async Task<Message> EditTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.EditMessageTextAsync(
            chatId,
            messageId,
            text,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            ct: ct);
    }

    public async Task<Message> EditCaptionAsync(
        long chatId,
        int messageId,
        string? caption,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        return await apiClient.EditMessageCaptionAsync(
            chatId,
            messageId,
            caption: caption ?? string.Empty,
            parseMode: parseMode,
            replyMarkup: replyMarkup,
            ct: ct);
    }

    public async Task DeleteAsync(long chatId, int messageId, CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        await apiClient.DeleteMessageAsync(chatId, messageId, ct);
    }

    public async Task AnswerCallbackAsync(
        string callbackQueryId,
        string? text = null,
        bool showAlert = false,
        CancellationToken ct = default)
    {
        var apiClient = await _botClientFactory.GetApiClientAsync();
        await apiClient.AnswerCallbackQueryAsync(callbackQueryId, text: text, showAlert: showAlert, ct: ct);
    }
}
