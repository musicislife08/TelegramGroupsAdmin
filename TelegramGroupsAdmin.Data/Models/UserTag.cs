using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for user_tags table
/// Quick classification labels for users using free-text tags
/// </summary>
[Table("user_tags")]
public class UserTagDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("telegram_user_id")]
    public long TelegramUserId { get; set; }

    [Column("tag_name")]
    [MaxLength(50)]
    public string TagName { get; set; } = string.Empty;  // Lowercase tag name

    // Exclusive Arc actor system (Phase 4.19)
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

    [Column("removed_at")]
    public DateTimeOffset? RemovedAt { get; set; }

    // Removal actor (when tag is removed)
    [Column("removed_by_web_user_id")]
    [MaxLength(450)]
    public string? RemovedByWebUserId { get; set; }

    [Column("removed_by_telegram_user_id")]
    public long? RemovedByTelegramUserId { get; set; }

    [Column("removed_by_system_identifier")]
    [MaxLength(50)]
    public string? RemovedBySystemIdentifier { get; set; }

    // Navigation property
    [ForeignKey(nameof(TelegramUserId))]
    public virtual TelegramUserDto? TelegramUser { get; set; }
}
