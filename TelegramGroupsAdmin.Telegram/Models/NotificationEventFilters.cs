using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Defines which event types should trigger notifications
/// Deserialized from notification_preferences.event_filters JSONB column
/// </summary>
public class NotificationEventFilters
{
    public bool SpamDetected { get; set; } = true;
    public bool SpamAutoDeleted { get; set; } = true;
    public bool UserBanned { get; set; } = true;
    public bool MessageReported { get; set; } = true;
    public bool ChatHealthWarning { get; set; } = true;
    public bool BackupFailed { get; set; } = true;
    public bool MalwareDetected { get; set; } = true;
    public bool ChatAdminChanged { get; set; } = true; // Phase 5.2: Enabled by default for security awareness

    /// <summary>
    /// Check if a specific event type should trigger notifications
    /// </summary>
    public bool IsEnabled(NotificationEventType eventType)
    {
        return eventType switch
        {
            NotificationEventType.SpamDetected => SpamDetected,
            NotificationEventType.SpamAutoDeleted => SpamAutoDeleted,
            NotificationEventType.UserBanned => UserBanned,
            NotificationEventType.MessageReported => MessageReported,
            NotificationEventType.ChatHealthWarning => ChatHealthWarning,
            NotificationEventType.BackupFailed => BackupFailed,
            NotificationEventType.MalwareDetected => MalwareDetected,
            NotificationEventType.ChatAdminChanged => ChatAdminChanged,
            _ => false
        };
    }
}
