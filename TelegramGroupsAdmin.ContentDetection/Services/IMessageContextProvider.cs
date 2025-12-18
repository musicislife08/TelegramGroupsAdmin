namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Provides message context for spam detection algorithms
/// </summary>
public interface IMessageContextProvider
{
    /// <summary>
    /// Get recent messages from a chat for context
    /// </summary>
    Task<IEnumerable<HistoryMessage>> GetRecentMessagesAsync(long chatId, int count = 10, CancellationToken cancellationToken = default);
}