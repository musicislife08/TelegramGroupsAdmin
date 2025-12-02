using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Abstractions.Jobs;

/// <summary>
/// Payload for sending chat notifications via Quartz job
/// Replaces fire-and-forget pattern in ReportService for reliable delivery
/// </summary>
public record SendChatNotificationPayload(
    long ChatId,
    NotificationEventType EventType,
    string Subject,
    string Message
);
