using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

// ============================================================================
// URL Filter Records - Phase 4.13
// ============================================================================

/// <summary>
/// EF Core entity for blocklist_subscriptions table
/// External URL-based blocklists (Block List Project + custom)
/// </summary>
[Table("blocklist_subscriptions")]
public class BlocklistSubscriptionDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; } = 0;  // 0 = global, non-zero = chat-specific

    [Column("name")]
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column("url")]
    [Required]
    [MaxLength(500)]
    public string Url { get; set; } = string.Empty;

    [Column("format")]
    public int Format { get; set; }  // BlocklistFormat enum

    [Column("block_mode")]
    public int BlockMode { get; set; }  // BlockMode enum

    [Column("is_built_in")]
    public bool IsBuiltIn { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    [Column("last_fetched")]
    public DateTimeOffset? LastFetched { get; set; }

    [Column("entry_count")]
    public int? EntryCount { get; set; }

    [Column("refresh_interval_hours")]
    public int RefreshIntervalHours { get; set; } = 24;

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
