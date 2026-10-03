using TelegramGroupsAdmin.Core.Models;
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
    Task<UiModels.MessageRecord?> GetMessageAsync(int messageId, long chatId, CancellationToken cancellationToken = default);
    Task UpdateMessageAsync(UiModels.MessageRecord message, CancellationToken cancellationToken = default);
    Task UpdateMediaLocalPathAsync(int messageId, long chatId, string localPath, CancellationToken cancellationToken = default);
    Task UpdateMessageTextAsync(int messageId, long chatId, string enrichedText, CancellationToken cancellationToken = default);
    Task UpdateMessageEditDateAsync(int messageId, long chatId, DateTimeOffset editDate, CancellationToken cancellationToken = default);
    Task MarkMessageAsDeletedAsync(int messageId, long chatId, string deletionSource, CancellationToken cancellationToken = default);

    // Message counts (used by impersonation detection and chat-specific queries)
    Task<int> GetMessageCountAsync(long userId, long chatId, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    // Cleanup (retention policy) - returns remaining stats for logging
    Task<UiModels.MessageCleanupResult> CleanupExpiredAsync(TimeSpan retention, CancellationToken cancellationToken = default);

    // Cross-chat ban cleanup (FEATURE-4.23)
    Task<List<UiModels.UserMessageInfo>> GetUserMessagesAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent messages in a chat along with each message's current verdict
    /// (message_verdicts view), newest first. Used to build AI veto history context so
    /// admin-corrected and sub-threshold-allowed messages are not shown as spam exemplars.
    /// <paramref name="excludeMessageId"/> (the message under evaluation) is left out before the
    /// window is taken, so the AI never sees its own row as history and still gets <paramref name="count"/> others.
    /// </summary>
    Task<IReadOnlyList<UiModels.RecentMessageVerdict>> GetRecentMessagesWithVerdictAsync(
        long chatId,
        int count,
        int? excludeMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current verdict (never a FileScan) for each requested message, keyed by message ID,
    /// for message-badge display. Messages with no verdict row are omitted.
    /// </summary>
    Task<Dictionary<int, UiModels.ContentCheckRecord>> GetCurrentContentChecksAsync(
        long chatId,
        IReadOnlyCollection<int> messageIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the perceptual-hash features of a message's photo/video on the message. No-op when the
    /// message is not stored.
    /// </summary>
    Task SetMediaFeaturesAsync(int messageId, long chatId, MediaFeatures features, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets messages whose current verdict is curated, that have no media features yet, and that
    /// carry a photo path or a video/animation/video-note path (startup media-feature backfill),
    /// newest decision first (stable order, so <paramref name="offset"/> pages past candidates the
    /// caller could not recover). A photo message reports <see cref="UiModels.MediaType.Photo"/>.
    /// </summary>
    Task<IReadOnlyList<UiModels.MediaBackfillCandidate>> GetMediaFeatureBackfillCandidatesAsync(int limit, int offset, CancellationToken cancellationToken = default);
}
