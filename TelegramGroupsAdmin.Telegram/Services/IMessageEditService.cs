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
    Task<List<UiModels.MessageEditRecord>> GetEditsForMessageAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get edit counts for multiple messages (used for batch display)
    /// </summary>
    Task<Dictionary<long, int>> GetEditCountsForMessagesAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default);
}
