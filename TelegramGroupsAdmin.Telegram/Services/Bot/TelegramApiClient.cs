using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Implementation of ITelegramApiClient that delegates to ITelegramBotClient extension methods.
/// Pure passthrough - no business logic, caching, or error handling.
/// </summary>
public class TelegramApiClient : ITelegramApiClient
{
    private readonly ITelegramBotClient _client;

    public TelegramApiClient(ITelegramBotClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BAN / MEMBERSHIP OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    public Task BanChatMemberAsync(long chatId, long userId, DateTime? untilDate = null, bool revokeMessages = true, CancellationToken ct = default)
        => _client.BanChatMember(chatId, userId, untilDate, revokeMessages, ct);

    public Task UnbanChatMemberAsync(long chatId, long userId, bool onlyIfBanned = true, CancellationToken ct = default)
        => _client.UnbanChatMember(chatId, userId, onlyIfBanned, ct);

    public Task RestrictChatMemberAsync(long chatId, long userId, ChatPermissions permissions, DateTime? untilDate = null, CancellationToken ct = default)
        => _client.RestrictChatMember(chatId, userId, permissions, untilDate: untilDate, cancellationToken: ct);

    public Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default)
        => _client.GetChatMember(chatId, userId, ct);

    // ═══════════════════════════════════════════════════════════════════════════
    // CHAT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default)
        => _client.GetChat(chatId, ct);

    public Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default)
        => _client.GetChatAdministrators(chatId, ct);

    public Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default)
        => _client.ExportChatInviteLink(chatId, ct);

    public Task LeaveChatAsync(long chatId, CancellationToken ct = default)
        => _client.LeaveChat(chatId, ct);

    // ═══════════════════════════════════════════════════════════════════════════
    // MESSAGE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<Message> SendMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.SendMessage(chatId, text, parseMode: parseMode ?? default, replyParameters: replyParameters, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.SendPhoto(chatId, photo, caption: caption, parseMode: parseMode ?? default, replyParameters: replyParameters, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.SendVideo(chatId, video, caption: caption, parseMode: parseMode ?? default, replyParameters: replyParameters, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task<Message> SendAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.SendAnimation(chatId, animation, caption: caption, parseMode: parseMode ?? default, replyParameters: replyParameters, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task<Message> EditMessageTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.EditMessageText(chatId, messageId, text, parseMode: parseMode ?? default, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task<Message> EditMessageCaptionAsync(
        long chatId,
        int messageId,
        string? caption = null,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default)
        => _client.EditMessageCaption(chatId, messageId, caption: caption, parseMode: parseMode ?? default, replyMarkup: replyMarkup, cancellationToken: ct);

    public Task DeleteMessageAsync(long chatId, int messageId, CancellationToken ct = default)
        => _client.DeleteMessage(chatId, messageId, ct);

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken ct = default)
        => _client.AnswerCallbackQuery(callbackQueryId, text: text, showAlert: showAlert, cancellationToken: ct);

    // ═══════════════════════════════════════════════════════════════════════════
    // USER / BOT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<User> GetMeAsync(CancellationToken ct = default)
        => _client.GetMe(ct);

    public Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int offset = 0, int limit = 100, CancellationToken ct = default)
        => _client.GetUserProfilePhotos(userId, offset: offset, limit: limit, cancellationToken: ct);

    // ═══════════════════════════════════════════════════════════════════════════
    // FILE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default)
        => _client.GetFile(fileId, ct);

    public Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default)
        => _client.DownloadFile(filePath, destination, ct);
}
