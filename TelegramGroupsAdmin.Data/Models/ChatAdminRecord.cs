using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for chat_admins table
/// Tracks Telegram admin status per chat for permission caching
/// </summary>
[Table("chat_admins")]
public class ChatAdminRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("telegram_id")]
    public long TelegramId { get; set; }

    [Column("username")]
    public string? Username { get; set; }

    [Column("is_creator")]
    public bool IsCreator { get; set; }

    [Column("promoted_at")]
    public DateTimeOffset PromotedAt { get; set; }

    [Column("last_verified_at")]
    public DateTimeOffset LastVerifiedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; }

    // Navigation property
    [ForeignKey(nameof(ChatId))]
    public virtual ManagedChatRecordDto? ManagedChat { get; set; }
}
