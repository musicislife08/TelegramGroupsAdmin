using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for core message CRUD operations
/// Extracted services: IMessageQueryService, IMessageStatsService, IMessageTranslationService, IMessageEditService
/// </summary>
public interface IMessageHistoryRepository
{
    // Core CRUD
    Task InsertMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default);
    Task<UiModels.MessageRecord?> GetMessageAsync(long messageId, CancellationToken cancellationToken = default);
    Task UpdateMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default);
    Task UpdateMediaLocalPathAsync(long messageId, string localPath, CancellationToken cancellationToken = default);
    Task UpdateMessageTextAsync(long messageId, string enrichedText, CancellationToken cancellationToken = default);
    Task UpdateMessageEditDateAsync(long messageId, DateTimeOffset editDate, CancellationToken cancellationToken = default);
    Task MarkMessageAsDeletedAsync(long messageId, string deletionSource, CancellationToken cancellationToken = default);

    // Message counts (used by impersonation detection and chat-specific queries)
    Task<int> GetMessageCountAsync(long userId, long chatId, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    // Cleanup (retention policy) - returns remaining stats for logging
    Task<UiModels.MessageCleanupResult> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default);

    // Cross-chat ban cleanup (FEATURE-4.23)
    Task<List<UiModels.UserMessageInfo>> GetUserMessagesAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    // SimHash deduplication
    /// <summary>
    /// Check if a similar SimHash exists in training data (detection_results with used_for_training=true OR training_labels).
    /// Uses COALESCE(translation.hash, message.hash) to prefer translated text hash.
    /// </summary>
    /// <param name="hash">The SimHash to compare against</param>
    /// <param name="isSpam">True to search spam training data, false for ham</param>
    /// <param name="maxDistance">Maximum Hamming distance to consider similar (default 10 = ~84% similarity)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a similar hash exists in training data</returns>
    Task<bool> HasSimilarTrainingHashAsync(long hash, bool isSpam, int maxDistance = 10, CancellationToken cancellationToken = default);
}
