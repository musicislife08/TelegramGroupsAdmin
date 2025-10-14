using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

// NOTE: Models are PUBLIC (not internal) because repositories live in the SpamDetection project
// which is a separate assembly. This is acceptable as they are just data containers.

// ============================================================================
// Training Samples
// ============================================================================

/// <summary>
/// EF Core entity for training_samples table
/// Public to allow cross-assembly repository usage
/// </summary>
[Table("training_samples")]
public class TrainingSample
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("message_text")]
    [Required]
    public string MessageText { get; set; } = string.Empty;

    [Column("is_spam")]
    public bool IsSpam { get; set; }

    [Column("added_date")]
    public long AddedDate { get; set; }

    [Column("source")]
    [Required]
    public string Source { get; set; } = string.Empty;

    [Column("confidence_when_added")]
    public int? ConfidenceWhenAdded { get; set; }

    [Column("chat_ids")]
    public long[]? ChatIds { get; set; }

    [Column("added_by")]
    public string? AddedBy { get; set; }

    [Column("detection_count")]
    public int DetectionCount { get; set; }

    [Column("last_detected_date")]
    public long? LastDetectedDate { get; set; }
}

/// <summary>
/// Public domain model for training statistics
/// </summary>
public class TrainingStats
{
    public int TotalSamples { get; set; }
    public int SpamSamples { get; set; }
    public int HamSamples { get; set; }
    public double SpamPercentage { get; set; }
    public Dictionary<string, int> SamplesBySource { get; set; } = new();
}

// ============================================================================
// Stop Words
// ============================================================================

/// <summary>
/// EF Core entity for stop_words table
/// Public to allow cross-assembly repository usage
/// </summary>
[Table("stop_words")]
public class StopWord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("word")]
    [Required]
    public string Word { get; set; } = string.Empty;

    [Column("enabled")]
    public bool Enabled { get; set; }

    [Column("added_date")]
    public long AddedDate { get; set; }

    [Column("added_by")]
    public string? AddedBy { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}

// ============================================================================
// Reports
// ============================================================================

/// <summary>
/// Report status enum
/// </summary>
public enum ReportStatus
{
    Pending = 0,
    Reviewed = 1,
    Dismissed = 2
}

/// <summary>
/// EF Core entity for reports table
/// Public to allow cross-assembly repository usage
/// Phase 2.6: Supports both Telegram /report and web UI "Flag for Review"
/// </summary>
[Table("reports")]
public class Report
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("report_command_message_id")]
    public int? ReportCommandMessageId { get; set; }  // NULL for web reports

    [Column("reported_by_user_id")]
    public long? ReportedByUserId { get; set; }       // NULL for web reports

    [Column("reported_by_user_name")]
    public string? ReportedByUserName { get; set; }

    [Column("reported_at")]
    public long ReportedAt { get; set; }

    [Column("status")]
    public int Status { get; set; }

    [Column("reviewed_by")]
    public string? ReviewedBy { get; set; }

    [Column("reviewed_at")]
    public long? ReviewedAt { get; set; }

    [Column("action_taken")]
    public string? ActionTaken { get; set; }

    [Column("admin_notes")]
    public string? AdminNotes { get; set; }

    [Column("web_user_id")]
    public string? WebUserId { get; set; }            // Phase 2.6: Web user ID (FK to users table)

    // Navigation property
    [ForeignKey(nameof(WebUserId))]
    public virtual UserRecord? WebUser { get; set; }
}
