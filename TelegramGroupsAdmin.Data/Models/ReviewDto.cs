using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for reviews table (unified review queue).
/// Handles Report, ImpersonationAlert, and ExamFailure review types.
/// Renamed from ReportDto as part of unified reviews migration.
/// </summary>
[Table("reviews")]
public class ReviewDto
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Discriminator for review type (Report, ImpersonationAlert, ExamFailure).
    /// </summary>
    [Column("type")]
    public ReviewType Type { get; set; } = ReviewType.Report;

    /// <summary>
    /// Type-specific context data stored as JSONB.
    /// Report: { "messageText": "...", "source": "telegram|web|system" }
    /// ImpersonationAlert: { "targetUserId": 123, "photoSimilarity": 0.85, ... }
    /// ExamFailure: { "mcAnswers": [...], "score": 67, ... }
    /// </summary>
    [Column("context")]
    public string? Context { get; set; }

    // === Report-specific fields (nullable for other types) ===

    [Column("message_id")]
    public int MessageId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    [Column("report_command_message_id")]
    public int? ReportCommandMessageId { get; set; }

    [Column("reported_by_user_id")]
    public long? ReportedByUserId { get; set; }

    [Column("reported_by_user_name")]
    public string? ReportedByUserName { get; set; }

    [Column("reported_at")]
    public DateTimeOffset ReportedAt { get; set; }

    // === Common review workflow fields ===

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
    public string? WebUserId { get; set; }

    // Navigation property
    [ForeignKey(nameof(WebUserId))]
    public virtual UserRecordDto? WebUser { get; set; }
}
