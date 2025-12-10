using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Mockable interface wrapping Telegram Bot API operations.
/// Abstracts the Telegram.Bot library's extension methods to enable unit testing with NSubstitute.
/// </summary>
/// <remarks>
/// The Telegram.Bot library uses extension methods (SendMessage, BanChatMember, etc.) which cannot
/// be mocked directly. This interface provides instance methods that delegate to the underlying
/// ITelegramBotClient, making services testable.
/// </remarks>
public interface ITelegramOperations
{
    // ─── Bot Info ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the bot's user ID. Available after the first API call.
    /// </summary>
    long BotId { get; }

    /// <summary>
    /// Get basic information about the bot.
    /// </summary>
    Task<User> GetMeAsync(CancellationToken ct = default);

    // ─── Messaging ────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a text message to a chat.
    /// </summary>
    Task<Message> SendMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>
    /// Edit text of an existing message.
    /// </summary>
    Task<Message> EditMessageTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete a message from a chat.
    /// </summary>
    Task DeleteMessageAsync(long chatId, int messageId, CancellationToken ct = default);

    /// <summary>
    /// Send a photo to a chat.
    /// </summary>
    Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send a video to a chat.
    /// </summary>
    Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        CancellationToken ct = default);

    // ─── Chat Info ────────────────────────────────────────────────────────────

    /// <summary>
    /// Get information about a chat.
    /// </summary>
    Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Get information about a member of a chat.
    /// </summary>
    Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default);

    /// <summary>
    /// Get a list of administrators in a chat.
    /// </summary>
    Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default);

    /// <summary>
    /// Get a user's profile photos.
    /// </summary>
    Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int limit = 1, CancellationToken ct = default);

    // ─── Moderation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ban a user from a chat (kick and prevent rejoin).
    /// </summary>
    Task BanChatMemberAsync(long chatId, long userId, DateTime? untilDate = null, CancellationToken ct = default);

    /// <summary>
    /// Unban a previously banned user in a chat.
    /// </summary>
    Task UnbanChatMemberAsync(long chatId, long userId, bool onlyIfBanned = true, CancellationToken ct = default);

    /// <summary>
    /// Restrict a user's permissions in a chat.
    /// </summary>
    Task RestrictChatMemberAsync(
        long chatId,
        long userId,
        ChatPermissions permissions,
        DateTime? untilDate = null,
        CancellationToken ct = default);

    // ─── File Operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Get basic info about a file and prepare it for downloading.
    /// </summary>
    Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Download a file from Telegram servers.
    /// </summary>
    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default);

    // ─── Callbacks & Admin ────────────────────────────────────────────────────

    /// <summary>
    /// Send an answer to a callback query.
    /// </summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default);

    /// <summary>
    /// Set the bot's command list.
    /// </summary>
    Task SetMyCommandsAsync(BotCommand[] commands, BotCommandScope? scope = null, CancellationToken ct = default);

    // ─── Invite Links ─────────────────────────────────────────────────────────

    /// <summary>
    /// Export the invite link for a chat.
    /// </summary>
    Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default);
}
