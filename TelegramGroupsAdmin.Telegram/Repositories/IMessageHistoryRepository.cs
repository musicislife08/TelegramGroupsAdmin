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
}
