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
    public long? ChatId { get; set; }

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

/// <summary>
/// EF Core entity for cached_blocked_domains table
/// Normalized, deduplicated domains from all enabled blocklists
/// Rebuilt periodically by BlocklistSyncService
/// </summary>
[Table("cached_blocked_domains")]
public class CachedBlockedDomainDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("domain")]
    [Required]
    [MaxLength(500)]
    public string Domain { get; set; } = string.Empty;

    [Column("block_mode")]
    public int BlockMode { get; set; }  // BlockMode enum (Hard=2, Soft=1)

    [Column("chat_id")]
    public long? ChatId { get; set; }  // NULL = global

    [Column("source_subscription_id")]
    public long? SourceSubscriptionId { get; set; }  // FK to blocklist_subscriptions, NULL for manual

    [Column("first_seen")]
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;

    [Column("last_verified")]
    public DateTimeOffset LastVerified { get; set; } = DateTimeOffset.UtcNow;

    [Column("notes")]
    public string? Notes { get; set; }
}
