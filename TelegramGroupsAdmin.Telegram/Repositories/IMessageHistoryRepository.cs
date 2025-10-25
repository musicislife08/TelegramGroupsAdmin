using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for message history operations
/// </summary>
public interface IMessageHistoryRepository
{
    Task InsertMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default);
    Task<UiModels.PhotoMessageRecord?> GetUserRecentPhotoAsync(long userId, long chatId, CancellationToken cancellationToken = default);
    Task<(int deletedCount, List<string> imagePaths, List<string> mediaPaths)> CleanupExpiredAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.MessageRecord>> GetRecentMessagesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<List<UiModels.MessageRecord>> GetMessagesBeforeAsync(DateTimeOffset? beforeTimestamp = null, int limit = 50, CancellationToken cancellationToken = default);
    Task<List<UiModels.MessageRecord>> GetMessagesByChatIdAsync(long chatId, int limit = 10, DateTimeOffset? beforeTimestamp = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get messages with detection history included (PERF-APP-1: Single JOIN query instead of N+1)
    /// </summary>
    Task<List<UiModels.MessageWithDetectionHistory>> GetMessagesWithDetectionHistoryAsync(long chatId, int limit = 10, DateTimeOffset? beforeTimestamp = null, CancellationToken cancellationToken = default);

    Task<List<UiModels.MessageRecord>> GetMessagesByDateRangeAsync(DateTimeOffset startDate, DateTimeOffset endDate, int limit = 1000, CancellationToken cancellationToken = default);
    Task<UiModels.HistoryStats> GetStatsAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<long, UiModels.SpamCheckRecord>> GetSpamChecksForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default);
    Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default);
    Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task InsertMessageEditAsync(UiModels.MessageEditRecord edit, CancellationToken cancellationToken = default);
    Task<UiModels.MessageRecord?> GetMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task<UiModels.MessageRecord?> GetByIdAsync(long messageId, CancellationToken cancellationToken = default); // Alias for GetMessageAsync
    Task UpdateMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default);
    Task UpdateMediaLocalPathAsync(long messageId, string localPath, CancellationToken cancellationToken = default);
    Task<List<string>> GetDistinctUserNamesAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetDistinctChatNamesAsync(CancellationToken cancellationToken = default);
    Task<UiModels.DetectionStats> GetDetectionStatsAsync(CancellationToken cancellationToken = default);
    Task<List<UiModels.DetectionResultRecord>> GetRecentDetectionsAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task MarkMessageAsDeletedAsync(long messageId, string deletionSource, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountAsync(long userId, long chatId, CancellationToken cancellationToken = default);

    // Translation methods (Phase 4.20)
    Task<UiModels.MessageTranslation?> GetTranslationForMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task<UiModels.MessageTranslation?> GetTranslationForEditAsync(long editId, CancellationToken cancellationToken = default);
    Task InsertTranslationAsync(UiModels.MessageTranslation translation, CancellationToken cancellationToken = default);

    // Analytics methods (UX-2)
    Task<UiModels.MessageTrendsData> GetMessageTrendsAsync(
        List<long> chatIds, // Empty = all accessible chats (filtered by caller)
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default);

    // Cross-chat ban cleanup (FEATURE-4.23)
    Task<List<UiModels.UserMessageInfo>> GetUserMessagesAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);
}
