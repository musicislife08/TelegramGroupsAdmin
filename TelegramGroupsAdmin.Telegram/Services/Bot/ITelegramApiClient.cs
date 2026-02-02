using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Thin wrapper around ITelegramBotClient extension methods to enable unit testing.
/// All methods delegate directly to the underlying client - no business logic.
///
/// Why this exists: Telegram.Bot uses extension methods for API calls (BanChatMember, SendMessage, etc.)
/// which NSubstitute cannot mock. This interface provides mockable virtual methods.
/// </summary>
public interface ITelegramApiClient
{
    // ═══════════════════════════════════════════════════════════════════════════
    // BAN / MEMBERSHIP OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Ban a user from a chat.</summary>
    Task BanChatMemberAsync(long chatId, long userId, DateTime? untilDate = null, bool revokeMessages = true, CancellationToken ct = default);

    /// <summary>Unban a user from a chat.</summary>
    Task UnbanChatMemberAsync(long chatId, long userId, bool onlyIfBanned = true, CancellationToken ct = default);

    /// <summary>Restrict a chat member's permissions.</summary>
    Task RestrictChatMemberAsync(long chatId, long userId, ChatPermissions permissions, DateTime? untilDate = null, CancellationToken ct = default);

    /// <summary>Get information about a chat member.</summary>
    Task<ChatMember> GetChatMemberAsync(long chatId, long userId, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // CHAT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get information about a chat.</summary>
    Task<ChatFullInfo> GetChatAsync(long chatId, CancellationToken ct = default);

    /// <summary>Get list of administrators in a chat.</summary>
    Task<ChatMember[]> GetChatAdministratorsAsync(long chatId, CancellationToken ct = default);

    /// <summary>Export the primary invite link for a chat.</summary>
    Task<string> ExportChatInviteLinkAsync(long chatId, CancellationToken ct = default);

    /// <summary>Leave a chat.</summary>
    Task LeaveChatAsync(long chatId, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // MESSAGE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Send a text message.</summary>
    Task<Message> SendMessageAsync(
        long chatId,
        string text,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send a photo.</summary>
    Task<Message> SendPhotoAsync(
        long chatId,
        InputFile photo,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send a video.</summary>
    Task<Message> SendVideoAsync(
        long chatId,
        InputFile video,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Send an animation (GIF).</summary>
    Task<Message> SendAnimationAsync(
        long chatId,
        InputFile animation,
        string? caption = null,
        ParseMode? parseMode = null,
        ReplyParameters? replyParameters = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Edit a text message.</summary>
    Task<Message> EditMessageTextAsync(
        long chatId,
        int messageId,
        string text,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Edit a message caption.</summary>
    Task<Message> EditMessageCaptionAsync(
        long chatId,
        int messageId,
        string? caption = null,
        ParseMode? parseMode = null,
        InlineKeyboardMarkup? replyMarkup = null,
        CancellationToken ct = default);

    /// <summary>Delete a message.</summary>
    Task DeleteMessageAsync(long chatId, int messageId, CancellationToken ct = default);

    /// <summary>Answer a callback query.</summary>
    Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // USER / BOT OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get basic info about the bot.</summary>
    Task<User> GetMeAsync(CancellationToken ct = default);

    /// <summary>Get a user's profile photos.</summary>
    Task<UserProfilePhotos> GetUserProfilePhotosAsync(long userId, int offset = 0, int limit = 100, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════
    // FILE OPERATIONS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Get file information for downloading.</summary>
    Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default);

    /// <summary>Download a file to a stream.</summary>
    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default);
}
