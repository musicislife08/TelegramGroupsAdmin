using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for telegram_link_tokens table
/// One-time tokens for linking Telegram accounts to web users
/// </summary>
[Table("telegram_link_tokens")]
public class TelegramLinkTokenRecordDto
{
    [Key]
    [Column("token")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("created_at")]
    public long CreatedAt { get; set; }

    [Column("expires_at")]
    public long ExpiresAt { get; set; }

    [Column("used_at")]
    public long? UsedAt { get; set; }

    [Column("used_by_telegram_id")]
    public long? UsedByTelegramId { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecordDto? User { get; set; }
}
