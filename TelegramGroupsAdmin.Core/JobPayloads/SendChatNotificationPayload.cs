using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for sending chat notifications via Quartz job
/// Replaces fire-and-forget pattern in ReportService for reliable delivery
/// </summary>
public record SendChatNotificationPayload(
    ChatIdentity Chat,
    NotificationEventType EventType,
    string Subject,
    string Message,
    /// <summary>Report ID for action buttons (optional)</summary>
    long? ReportId = null,
    /// <summary>Absolute path to photo file for DM with image (optional)</summary>
    string? PhotoPath = null,
    /// <summary>Reported user's Telegram ID for moderation actions (optional)</summary>
    long? ReportedUserId = null,
    /// <summary>Report type for building correct action buttons (optional)</summary>
    ReportType? ReportType = null
);
