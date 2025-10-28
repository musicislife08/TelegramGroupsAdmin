using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for invites table
/// </summary>
[Table("invites")]
public class InviteRecordDto
{
    [Key]
    [Column("token")]
    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    [Column("created_by")]
    [Required]
    public string CreatedBy { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [Column("used_by")]
    public string? UsedBy { get; set; }

    [Column("permission_level")]
    public PermissionLevel PermissionLevel { get; set; }

    [Column("status")]
    public InviteStatus Status { get; set; }

    [Column("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    // Navigation properties
    [ForeignKey(nameof(CreatedBy))]
    public virtual UserRecordDto? Creator { get; set; }

    [ForeignKey(nameof(UsedBy))]
    public virtual UserRecordDto? UsedByUser { get; set; }
}
