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
    /// Get messages before a timestamp for cursor-based pagination
    /// </summary>
    Task<List<UiModels.MessageRecord>> GetMessagesBeforeAsync(
        DateTimeOffset? beforeTimestamp = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

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
    /// Get messages within a date range
    /// </summary>
    Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(
        DateTimeOffset startTimestamp,
        DateTimeOffset endTimestamp,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get content checks for multiple messages (latest detection per message)
    /// </summary>
    Task<Dictionary<long, UiModels.ContentCheckRecord>> GetContentChecksForMessagesAsync(
        IEnumerable<long> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get distinct usernames from telegram_users table
    /// </summary>
    Task<List<string>> GetDistinctUserNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get distinct chat names from managed_chats table
    /// </summary>
    Task<List<string>> GetDistinctChatNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's most recent photo in a specific chat
    /// </summary>
    Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(
        long userId,
        long chatId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get last message preview for multiple chats (for sidebar display).
    /// Returns a dictionary of chatId -> preview data.
    /// </summary>
    Task<Dictionary<long, UiModels.ChatMessagePreview>> GetLastMessagePreviewsAsync(
        IEnumerable<long> chatIds,
        CancellationToken cancellationToken = default);
}
