using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

// NOTE: Models are PUBLIC (not internal) because repositories live in the SpamDetection project
// which is a separate assembly. This is acceptable as they are just data containers.

/// <summary>
/// EF Core entity for stop_words table
/// Public to allow cross-assembly repository usage
/// </summary>
[Table("stop_words")]
public class StopWordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("word")]
    [Required]
    public string Word { get; set; } = string.Empty;

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("added_date")]
    public DateTimeOffset AddedDate { get; set; }

    // Exclusive Arc actor system (Phase 4.19) - exactly one must be non-null
    [Column("web_user_id")]
    [MaxLength(450)]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    [MaxLength(50)]
    public string? SystemIdentifier { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}
