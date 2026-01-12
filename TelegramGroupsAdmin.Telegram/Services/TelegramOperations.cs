using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Implementation of ITelegramOperations that delegates to the underlying ITelegramBotClient.
/// This class enables unit testing by wrapping extension methods in mockable instance methods.
/// </summary>
/// <remarks>
/// Created and managed by TelegramBotClientFactory - do not instantiate directly.
/// The factory ensures this wrapper stays in sync with the underlying client when the bot token changes.
/// </remarks>
public class TelegramOperations : ITelegramOperations
{
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<TelegramOperations> _logger;

    public TelegramOperations(ITelegramBotClient botClient, ILogger<TelegramOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(botClient);
        ArgumentNullException.ThrowIfNull(logger);

        _botClient = botClient;
        _logger = logger;
    }

    // ─── Bot Info ─────────────────────────────────────────────────────────────

    public long BotId => _botClient.BotId;

    public async Task<User> GetMeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting bot info");
        return await _botClient.GetMe(cancellationToken);
    }

    // ─── Messaging ────────────────────────────────────────────────────────────

    public async Task<Message> SendMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending message to chat {ChatId} ({Length} chars)", chatId, text.Length);
        return await _botClient.SendMessage(
            chatId,
            text,
            parseMode: parseMode ?? default,
            replyParameters: replyParameters,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }

    public Task<Message> EditMessageTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
        => _botClient.EditMessageText(
            chatId,
            messageId,
            text,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

    public Task<Message> EditMessageCaptionAsync(
        long chatId,
        int messageId,
        string caption,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
        => _botClient.EditMessageCaption(
            chatId,
            messageId,
            caption,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

    public async Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deleting message {MessageId} from chat {ChatId}", messageId, chatId);
        await _botClient.DeleteMessage(chatId, messageId, cancellationToken);
    }

    public Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default)
        => _botClient.SendPhoto(
            chatId,
            photo,
            caption: caption,
            parseMode: parseMode ?? default,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);

    public Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default)
        => _botClient.SendVideo(
            chatId,
            video,
            caption: caption,
            parseMode: parseMode ?? default,
            cancellationToken: cancellationToken);

    // ─── Chat Info ────────────────────────────────────────────────────────────

    public Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken cancellationToken = default)
        => _botClient.GetChat(chatId, cancellationToken);

    public Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken cancellationToken = default)
        => _botClient.GetChatMember(chatId, userId, cancellationToken);

    public Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken cancellationToken = default)
        => _botClient.GetChatAdministrators(chatId, cancellationToken);

    public Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int limit = 1, CancellationToken cancellationToken = default)
        => _botClient.GetUserProfilePhotos(userId, limit: limit, cancellationToken: cancellationToken);

    // ─── Moderation ───────────────────────────────────────────────────────────

    public async Task BanChatMemberAsync(long chatId, long userId, DateTime? untilDate = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Banning user {UserId} from chat {ChatId}{Until}",
            userId, chatId, untilDate.HasValue ? $" until {untilDate:u}" : " permanently");
        await _botClient.BanChatMember(chatId, userId, untilDate: untilDate, cancellationToken: cancellationToken);
    }

    public async Task UnbanChatMemberAsync(long chatId, long userId, bool onlyIfBanned = true, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Unbanning user {UserId} from chat {ChatId} (onlyIfBanned: {OnlyIfBanned})",
            userId, chatId, onlyIfBanned);
        await _botClient.UnbanChatMember(chatId, userId, onlyIfBanned: onlyIfBanned, cancellationToken: cancellationToken);
    }

    public async Task RestrictChatMemberAsync(
        long chatId,
        long userId,
        ChatPermissions permissions,
        DateTime? untilDate = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restricting user {UserId} in chat {ChatId}{Until}",
            userId, chatId, untilDate.HasValue ? $" until {untilDate:u}" : " permanently");
        await _botClient.RestrictChatMember(
            chatId,
            userId,
            permissions,
            untilDate: untilDate,
            cancellationToken: cancellationToken);
    }

    // ─── File Operations ──────────────────────────────────────────────────────

    public async Task<TGFile> GetFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting file info for {FileId}", fileId);
        return await _botClient.GetFile(fileId, cancellationToken);
    }

    public async Task DownloadFileAsync(string filePath, Stream destination, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Downloading file from {FilePath}", filePath);
        await _botClient.DownloadFile(filePath, destination, cancellationToken);
    }

    // ─── Callbacks & Admin ────────────────────────────────────────────────────

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken cancellationToken = default)
        => _botClient.AnswerCallbackQuery(callbackQueryId, text: text, cancellationToken: cancellationToken);

    public Task SetMyCommandsAsync(BotCommand[] commands, BotCommandScope? scope = null, CancellationToken cancellationToken = default)
        => _botClient.SetMyCommands(commands, scope: scope, cancellationToken: cancellationToken);

    // ─── Invite Links ─────────────────────────────────────────────────────────

    public Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken cancellationToken = default)
        => _botClient.ExportChatInviteLink(chatId, cancellationToken);

    // ─── Chat Management ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task LeaveChatAsync(long chatId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Leaving chat {ChatId}", chatId);
        await _botClient.LeaveChat(chatId, cancellationToken);
    }
}
