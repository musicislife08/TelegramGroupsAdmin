using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for complex message queries with JOINs and enrichment
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public interface IMessageQueryService
{
    /// <summary>
    /// Get recent messages with enriched data (chat name, user info, reply context)
    /// </summary>
    Task<List<UiModels.MessageRecord>> GetRecentMessagesAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages for a specific chat with pagination
    /// </summary>
    Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(
        long chatId,
        int limit = 10,
        DateTimeOffset? beforeTimestamp = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages with detection history, tags, and notes (PERF-APP-1 optimization)
    /// </summary>
    Task<List<UiModels.MessageWithDetectionHistory>> GetMessagesWithDetectionHistoryAsync(
        long chatId,
        int limit = 10,
        DateTimeOffset? beforeTimestamp = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single message with detection history by message ID.
    /// Used by orchestrator to build rich notifications after spam ban.
    /// </summary>
    Task<UiModels.MessageWithDetectionHistory?> GetMessageWithDetectionHistoryAsync(
        int messageId,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get content checks for multiple messages (latest detection per message)
    /// </summary>
    Task<Dictionary<int, UiModels.ContentCheckRecord>> GetContentChecksForMessagesAsync(
        long chatId,
        IEnumerable<int> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's most recent photo in a specific chat
    /// </summary>
    Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(
        long userId,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single message by ID with full enrichment (user photo, reply context).
    /// Used for real-time message notifications where bare message needs enrichment.
    /// </summary>
    /// <param name="message">The bare message record (used for IDs and logging context)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enriched message with UserPhotoPath, ReplyToUser, ReplyToText populated</returns>
    Task<UiModels.MessageRecord?> GetMessageByIdAsync(
        UiModels.MessageRecord message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get paginated messages for a specific user across accessible chats.
    /// Used by the per-user messages dialog for cross-chat message history.
    /// </summary>
    Task<List<UiModels.MessageRecord>> GetUserMessagesPaginatedAsync(
        long telegramUserId,
        IReadOnlyCollection<long> accessibleChatIds,
        int limit = 50,
        DateTimeOffset? beforeTimestamp = null,
        CancellationToken cancellationToken = default);
}
