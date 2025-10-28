namespace TelegramGroupsAdmin.ContentDetection.Services;

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
