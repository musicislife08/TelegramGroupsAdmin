namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Domain model for in-app web notifications
/// Used by the Web Push channel in the notification system
/// </summary>
public class WebNotification
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationEventType EventType { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
