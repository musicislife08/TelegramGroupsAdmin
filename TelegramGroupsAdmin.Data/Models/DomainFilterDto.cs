using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for domain_filters table
/// Manual domain entries (blacklist/whitelist)
/// </summary>
[Table("domain_filters")]
public class DomainFilterDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long? ChatId { get; set; }

    [Column("domain")]
    [Required]
    [MaxLength(500)]
    public string Domain { get; set; } = string.Empty;

    [Column("filter_type")]
    public int FilterType { get; set; }  // DomainFilterType enum

    [Column("block_mode")]
    public int BlockMode { get; set; }  // BlockMode enum (ignored for whitelist)

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    // Actor system (Phase 4.19)
    [Column("web_user_id")]
    [MaxLength(450)]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    [MaxLength(50)]
    public string? SystemIdentifier { get; set; }

    [Column("added_date")]
    public DateTimeOffset AddedDate { get; set; } = DateTimeOffset.UtcNow;

    [Column("notes")]
    public string? Notes { get; set; }
}
