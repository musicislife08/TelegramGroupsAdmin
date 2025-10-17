using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for admin_notes table
/// Stores admin annotations on Telegram users for internal reference
/// </summary>
[Table("admin_notes")]
public class AdminNoteDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [Column("note_text")]
    [MaxLength(1000)]
    public string NoteText { get; set; } = string.Empty;

    // Legacy actor field (will be dropped in future migration after data migration)
    [Column("created_by")]
    [MaxLength(255)]
    public string CreatedBy { get; set; } = string.Empty;  // Web user ID or "telegram:@username"

    // New Exclusive Arc actor system (Phase 4.19)
    [Column("actor_web_user_id")]
    [MaxLength(450)]
    public string? ActorWebUserId { get; set; }

    [Column("actor_telegram_user_id")]
    public long? ActorTelegramUserId { get; set; }

    [Column("actor_system_identifier")]
    [MaxLength(50)]
    public string? ActorSystemIdentifier { get; set; }

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [Column("is_pinned")]
    public bool IsPinned { get; set; }  // Pin important notes to top

    // Navigation property
    [ForeignKey(nameof(TelegramUserId))]
    public virtual TelegramUserDto? TelegramUser { get; set; }
}
