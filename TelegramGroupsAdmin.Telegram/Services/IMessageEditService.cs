using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services;

/// <summary>
/// Service for managing message edit history
/// Extracted from MessageHistoryRepository (REFACTOR-3)
/// </summary>
public interface IMessageEditService
{
    /// <summary>
    /// Insert a message edit record
    /// </summary>
    Task InsertMessageEditAsync(UiModels.MessageEditRecord edit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all edits for a specific message
    /// </summary>
    Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(int messageId, long chatId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get edit counts for multiple messages (used for batch display)
    /// </summary>
    Task<Dictionary<int, int>> GetEditCountsForMessagesAsync(long chatId, IEnumerable<int> messageIds, CancellationToken cancellationToken = default);
}
