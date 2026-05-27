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
    public int MatchType { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("notes")]
    [MaxLength(500)]
    public string? Notes { get; set; }
}
