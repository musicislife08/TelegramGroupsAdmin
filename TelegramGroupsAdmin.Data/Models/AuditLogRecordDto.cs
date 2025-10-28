using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for audit_log table
/// </summary>
[Table("audit_log")]
public class AuditLogRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("event_type")]
    public AuditEventType EventType { get; set; }

    [Column("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    // ===== Legacy Fields (to be dropped after migration) =====
    [Column("actor_user_id")]
    public string? ActorUserId { get; set; }        // Legacy - who performed the action (web user ID only)

    [Column("target_user_id")]
    public string? TargetUserId { get; set; }       // Legacy - who was affected (web/telegram user ID mixed)

    // ===== Actor Exclusive Arc (ARCH-2 migration) =====
    // Exactly one of these should be non-null (enforced by DB check constraint)
    [Column("actor_web_user_id")]
    public string? ActorWebUserId { get; set; }     // Web admin user ID (FK to users)

    [Column("actor_telegram_user_id")]
    public long? ActorTelegramUserId { get; set; }  // Telegram user ID (FK to telegram_users)

    [Column("actor_system_identifier")]
    public string? ActorSystemIdentifier { get; set; } // System/automation identifier (e.g., "auto_trust", "spam_detector")

    // ===== Target Exclusive Arc (ARCH-2 migration) =====
    // Exactly one of these should be non-null when target is a user (enforced by DB check constraint)
    [Column("target_web_user_id")]
    public string? TargetWebUserId { get; set; }    // Web admin user ID (FK to users)

    [Column("target_telegram_user_id")]
    public long? TargetTelegramUserId { get; set; } // Telegram user ID (FK to telegram_users)

    [Column("target_system_identifier")]
    public string? TargetSystemIdentifier { get; set; } // System/automation identifier (rare, for system-to-system events)

    [Column("value")]
    public string? Value { get; set; }              // Context/relevant data (e.g., new status, permission level, etc.)
}
