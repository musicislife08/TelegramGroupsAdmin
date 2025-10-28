namespace TelegramGroupsAdmin.ContentDetection.Services;

/// <summary>
/// Service for retrieving message history context for spam detection
/// </summary>
public interface IMessageHistoryService
{
    /// <summary>
    /// Get recent messages from a chat for context
    /// </summary>
    Task<IEnumerable<HistoryMessage>> GetRecentMessagesAsync(long chatId, int count = 10, CancellationToken cancellationToken = default);
}