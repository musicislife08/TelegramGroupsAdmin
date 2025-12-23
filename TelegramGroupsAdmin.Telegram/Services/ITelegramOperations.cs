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
    Task<User> GetMeAsync(CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edit text of an existing message.
    /// </summary>
    Task<Message> EditMessageTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a message from a chat.
    /// </summary>
    Task DeleteMessageAsync(long chatId, int messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a photo to a chat.
    /// </summary>
    Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyMarkup? replyMarkup = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a video to a chat.
    /// </summary>
    Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        CancellationToken cancellationToken = default);

    // ─── Chat Info ────────────────────────────────────────────────────────────

    /// <summary>
    /// Get information about a chat.
    /// </summary>
    Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about a member of a chat.
    /// </summary>
    Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a list of administrators in a chat.
    /// </summary>
    Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a user's profile photos.
    /// </summary>
    Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int limit = 1, CancellationToken cancellationToken = default);

    // ─── Moderation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Ban a user from a chat (kick and prevent rejoin).
    /// </summary>
    Task BanChatMemberAsync(long chatId, long userId, DateTime? untilDate = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unban a previously banned user in a chat.
    /// </summary>
    Task UnbanChatMemberAsync(long chatId, long userId, bool onlyIfBanned = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restrict a user's permissions in a chat.
    /// </summary>
    Task RestrictChatMemberAsync(
        long chatId,
        long userId,
        ChatPermissions permissions,
        DateTime? untilDate = null,
        CancellationToken cancellationToken = default);

    // ─── File Operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Get basic info about a file and prepare it for downloading.
    /// </summary>
    Task<TGFile> GetFileAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file from Telegram servers.
    /// </summary>
    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken cancellationToken = default);

    // ─── Callbacks & Admin ────────────────────────────────────────────────────

    /// <summary>
    /// Send an answer to a callback query.
    /// </summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the bot's command list.
    /// </summary>
    Task SetMyCommandsAsync(BotCommand[] commands, BotCommandScope? scope = null, CancellationToken cancellationToken = default);

    // ─── Invite Links ─────────────────────────────────────────────────────────

    /// <summary>
    /// Export the invite link for a chat.
    /// </summary>
    Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken cancellationToken = default);

    // ─── Chat Management ───────────────────────────────────────────────────────

    /// <summary>
    /// Leave a chat (group, supergroup, or channel).
    /// </summary>
    Task LeaveChatAsync(long chatId, CancellationToken cancellationToken = default);
}
