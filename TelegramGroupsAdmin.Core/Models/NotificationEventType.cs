namespace TelegramGroupsAdmin.Core.Models;

/// <summary>
/// Notification event types (Core domain model)
/// Phase 5.1: Defines all notification event categories
/// </summary>
public enum NotificationEventType
{
    SpamDetected,
    SpamAutoDeleted,
    UserBanned,
    MessageReported,
    ChatHealthWarning,
    BackupFailed,
    MalwareDetected,
    ChatAdminChanged // Phase 5.2: Notify owners when admins are promoted/demoted
}
