using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for recovery_codes table
/// </summary>
[Table("recovery_codes")]
public class RecoveryCodeRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Column("code_hash")]
    [Required]
    public string CodeHash { get; set; } = string.Empty;

    [Column("used_at")]
    public DateTimeOffset? UsedAt { get; set; }

    // Navigation property
    [ForeignKey(nameof(UserId))]
    public virtual UserRecordDto? User { get; set; }
}
