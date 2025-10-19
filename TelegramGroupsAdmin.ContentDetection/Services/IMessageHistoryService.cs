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

/// <summary>
/// Message history data for context
/// </summary>
public record HistoryMessage
{
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public bool WasSpam { get; init; }
}