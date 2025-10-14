using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for telegram_user_mappings table
/// Maps Telegram users to web app users for permission checking
/// </summary>
[Table("telegram_user_mappings")]
public class TelegramUserMappingRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("telegram_id")]
    public long TelegramId { get; set; }

    [Column("telegram_username")]
    public string? TelegramUsername { get; set; }

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("linked_at")]
    public long LinkedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecord? User { get; set; }
}
