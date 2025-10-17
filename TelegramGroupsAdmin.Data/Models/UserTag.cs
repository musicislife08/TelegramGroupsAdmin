using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Tag categories for user classification
/// </summary>
public enum TagType
{
    Suspicious = 0,           // User shows suspicious behavior
    VerifiedContributor = 1,  // High-quality contributor
    SpamRisk = 2,             // Shows spam patterns but not banned yet
    SuspectedBot = 3,         // May be a bot account (not confirmed)
    Impersonator = 4,         // Attempting to impersonate others
    Helpful = 5,              // Helpful community member
    Moderator = 6,            // Community moderator (not admin)
    Custom = 99               // Custom tag with free text
}

/// <summary>
/// EF Core entity for user_tags table
/// Quick classification labels for users (suspicious, verified, etc.)
/// </summary>
[Table("user_tags")]
public class UserTagDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [Column("tag_type")]
    public TagType TagType { get; set; }

    [Column("tag_label")]
    [MaxLength(50)]
    public string? TagLabel { get; set; }  // For custom tags or override display

    // Legacy actor field (will be dropped in future migration after data migration)
    [Column("added_by")]
    [MaxLength(255)]
    public string AddedBy { get; set; } = string.Empty;

    // New Exclusive Arc actor system (Phase 4.19)
    [Column("actor_web_user_id")]
    [MaxLength(450)]
    public string? ActorWebUserId { get; set; }

    [Column("actor_telegram_user_id")]
    public long? ActorTelegramUserId { get; set; }

    [Column("actor_system_identifier")]
    [MaxLength(50)]
    public string? ActorSystemIdentifier { get; set; }

    [Column("added_at")]
    public DateTimeOffset AddedAt { get; set; }

    [Column("confidence_modifier")]
    public int? ConfidenceModifier { get; set; }  // Optional: Adjust spam confidence (+10 for suspicious, -20 for verified)

    // Navigation property
    [ForeignKey(nameof(TelegramUserId))]
    public virtual TelegramUserDto? TelegramUser { get; set; }
}
