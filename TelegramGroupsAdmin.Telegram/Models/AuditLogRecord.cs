namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Audit log record for UI display
/// </summary>
public record AuditLogRecord(
    long Id,
    AuditEventType EventType,
    DateTimeOffset Timestamp,
    string? ActorUserId,
    string? TargetUserId,
    string? Value
);
