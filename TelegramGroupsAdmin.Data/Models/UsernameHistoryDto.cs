using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Tracks historical username/name values for a Telegram user.
/// Each row captures the previous values at the moment a profile change is detected.
/// </summary>
[Table("username_history")]
public class UsernameHistoryDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("username")]
    [MaxLength(32)]
    public string? Username { get; set; }

    [Column("first_name")]
    [MaxLength(64)]
    public string? FirstName { get; set; }

    [Column("last_name")]
    [MaxLength(64)]
    public string? LastName { get; set; }

    [Column("recorded_at")]
    public DateTimeOffset RecordedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public virtual TelegramUserDto? User { get; set; }
}
