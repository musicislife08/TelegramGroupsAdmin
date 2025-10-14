using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for detection_results table
/// </summary>
[Table("detection_results")]
public class DetectionResultRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("message_id")]
    public long MessageId { get; set; }

    [Column("detected_at")]
    public long DetectedAt { get; set; }

    [Column("detection_source")]
    [Required]
    [MaxLength(50)]
    public string DetectionSource { get; set; } = string.Empty;

    [Column("detection_method")]
    [Required]
    public string DetectionMethod { get; set; } = string.Empty;

    [Column("is_spam")]
    public bool IsSpam { get; set; }

    [Column("confidence")]
    public int Confidence { get; set; }

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("added_by")]
    public string? AddedBy { get; set; }

    [Column("used_for_training")]
    public bool UsedForTraining { get; set; } = true;

    [Column("net_confidence")]
    public int? NetConfidence { get; set; }

    [Column("check_results_json")]
    public string? CheckResultsJson { get; set; }

    [Column("edit_version")]
    public int EditVersion { get; set; }

    // Navigation property
    [ForeignKey(nameof(MessageId))]
    public virtual MessageRecord? Message { get; set; }
}
