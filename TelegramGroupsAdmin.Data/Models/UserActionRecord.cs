using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// User action type (Data layer - stored as INT in database)
/// </summary>
public enum UserActionType
{
    Ban = 0,
    Warn = 1,
    Mute = 2,
    Trust = 3,
    Unban = 4
}

/// <summary>
/// EF Core entity for user_actions table
/// All actions are global - origin chat can be tracked via message_id FK
/// </summary>
[Table("user_actions")]
public class UserActionRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("action_type")]
    public UserActionType ActionType { get; set; }

    [Column("message_id")]
    public long? MessageId { get; set; }

    // Exclusive Arc actor system (Phase 4.19) - exactly one must be non-null
    [Column("web_user_id")]
    [MaxLength(450)]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    [MaxLength(50)]
    public string? SystemIdentifier { get; set; }

    [Column("issued_at")]
    public DateTimeOffset IssuedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    // Navigation property
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecordDto? Message { get; set; }
}
