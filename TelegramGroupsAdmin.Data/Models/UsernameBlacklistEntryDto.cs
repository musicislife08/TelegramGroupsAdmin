using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for username_blacklist table.
/// Stores display name patterns that trigger auto-ban on join.
/// </summary>
[Table("username_blacklist")]
public class UsernameBlacklistEntryDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("pattern")]
    [Required]
    [MaxLength(200)]
    public string Pattern { get; set; } = string.Empty;

    [Column("match_type")]
    public int MatchType { get; set; } // BlacklistMatchType as int

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    // Exclusive Arc actor system - exactly one must be non-null
    [Column("web_user_id")]
    [MaxLength(450)]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    [MaxLength(50)]
    public string? SystemIdentifier { get; set; }

    [Column("notes")]
    [MaxLength(500)]
    public string? Notes { get; set; }
}
