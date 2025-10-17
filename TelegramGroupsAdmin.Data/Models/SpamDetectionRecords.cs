using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

// NOTE: Models are PUBLIC (not internal) because repositories live in the SpamDetection project
// which is a separate assembly. This is acceptable as they are just data containers.

// ============================================================================
// OBSOLETE: Training Samples table was removed in Phase 2.2
// Training data now comes from detection_results.used_for_training = true
// ============================================================================

// ============================================================================
// Stop Words
// ============================================================================

/// <summary>
/// EF Core entity for stop_words table
/// Public to allow cross-assembly repository usage
/// </summary>
[Table("stop_words")]
public class StopWordDto
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
    public DateTimeOffset AddedDate { get; set; }

    // Exclusive Arc actor system (Phase 4.19) - exactly one must be non-null
    [Column("web_user_id")]
    [MaxLength(450)]
    public string? WebUserId { get; set; }

    [Column("telegram_user_id")]
    public long? TelegramUserId { get; set; }

    [Column("system_identifier")]
    [MaxLength(50)]
    public string? SystemIdentifier { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for stop word with user email (used in JOIN queries)
/// Not an EF entity - just a DTO for query results
/// </summary>
public class StopWordWithEmailDto
{
    public StopWordDto StopWord { get; set; } = null!;
    public string? AddedByEmail { get; set; }
}

// ============================================================================
// Reports
// ============================================================================

/// <summary>
/// Report status (Data layer - stored as INT in database)
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
public class ReportDto
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
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
    public DateTimeOffset ReportedAt { get; set; }

    [Column("status")]
    public ReportStatus Status { get; set; }

    [Column("reviewed_by")]
    public string? ReviewedBy { get; set; }

    [Column("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    [Column("action_taken")]
    public string? ActionTaken { get; set; }

    [Column("admin_notes")]
    public string? AdminNotes { get; set; }

    [Column("web_user_id")]
    public string? WebUserId { get; set; }            // Phase 2.6: Web user ID (FK to users table)

    // Navigation property
    [ForeignKey(nameof(WebUserId))]
    public virtual UserRecordDto? WebUser { get; set; }
}
