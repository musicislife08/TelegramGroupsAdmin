using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

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
    public long ChatId { get; set; } = 0;  // 0 = global, non-zero = chat-specific

    [Column("source_subscription_id")]
    public long? SourceSubscriptionId { get; set; }  // FK to blocklist_subscriptions, NULL for manual

    [Column("first_seen")]
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;

    [Column("last_verified")]
    public DateTimeOffset LastVerified { get; set; } = DateTimeOffset.UtcNow;

    [Column("notes")]
    public string? Notes { get; set; }
}
