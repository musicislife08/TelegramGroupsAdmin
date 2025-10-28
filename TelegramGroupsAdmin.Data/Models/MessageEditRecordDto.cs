using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for message_edits table
/// </summary>
[Table("message_edits")]
public class MessageEditRecordDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("edit_date")]
    public DateTimeOffset EditDate { get; set; }

    [Column("old_text")]
    public string? OldText { get; set; }

    [Column("new_text")]
    public string? NewText { get; set; }

    [Column("old_content_hash")]
    [MaxLength(64)]
    public string? OldContentHash { get; set; }

    [Column("new_content_hash")]
    [MaxLength(64)]
    public string? NewContentHash { get; set; }

    // Navigation property
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecordDto? Message { get; set; }
}
