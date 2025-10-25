namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Audit log record for UI display
/// </summary>
public record AuditLogRecord(
    long Id,
    AuditEventType EventType,
    DateTimeOffset Timestamp,

    // Actor exclusive arc (ARCH-2)
    string? ActorWebUserId,
    long? ActorTelegramUserId,
    string? ActorSystemIdentifier,

    // Target exclusive arc (ARCH-2)
    string? TargetWebUserId,
    long? TargetTelegramUserId,
    string? TargetSystemIdentifier,

    string? Value
);
