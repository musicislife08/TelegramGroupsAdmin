using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for training_labels table.
/// Stores explicit spam/ham labels for ML training (separate from detection_results history).
/// </summary>
[Table("training_labels")]
public class TrainingLabelDto
{
    /// <summary>
    /// Message ID (primary key). One label per message.
    /// </summary>
    [Key]
    [Column("message_id")]
    public long MessageId { get; set; }

    /// <summary>
    /// Label: "spam" or "ham". Enforced by database check constraint.
    /// </summary>
    [Required]
    [Column("label")]
    [MaxLength(10)]
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// User ID who labeled this message (nullable for migrated/system labels).
    /// </summary>
    [Column("labeled_by_user_id")]
    public long? LabeledByUserId { get; set; }

    /// <summary>
    /// Timestamp when label was created/updated.
    /// </summary>
    [Column("labeled_at")]
    public DateTimeOffset LabeledAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional reason/explanation for the label.
    /// </summary>
    [Column("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Optional audit log ID linking to the moderation action that created this label.
    /// </summary>
    [Column("audit_log_id")]
    public long? AuditLogId { get; set; }

    // Navigation properties
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecordDto? Message { get; set; }

    [ForeignKey(nameof(LabeledByUserId))]
    public virtual TelegramUserDto? LabeledByUser { get; set; }
}
