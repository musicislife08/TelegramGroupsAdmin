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
    Task MarkMessageAsDeletedAsync(long messageId, string deletionSource, CancellationToken cancellationToken = default);

    // Message counts (used by impersonation detection and chat-specific queries)
    Task<int> GetMessageCountAsync(long userId, long chatId, CancellationToken cancellationToken = default);
    Task<int> GetMessageCountByChatIdAsync(long chatId, CancellationToken cancellationToken = default);

    // Cleanup (retention policy)
    Task<(int deletedCount, List<string> imagePaths, List<string> mediaPaths)> CleanupExpiredAsync(CancellationToken cancellationToken = default);

    // Cross-chat ban cleanup (FEATURE-4.23)
    Task<List<UiModels.UserMessageInfo>> GetUserMessagesAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);
}
