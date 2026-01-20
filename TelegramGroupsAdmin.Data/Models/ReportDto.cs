using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for reports table (unified report queue).
/// Handles Report, ImpersonationAlert, and ExamFailure types.
/// </summary>
[Table("reports")]
public class ReportDto
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Discriminator for report type. Repository maps to domain enum.
    /// 0=ContentReport, 1=ImpersonationAlert, 2=ExamFailure
    /// </summary>
    [Column("type")]
    public short Type { get; set; }

    /// <summary>
    /// Type-specific context data stored as JSONB.
    /// </summary>
    [Column("context")]
    public string? Context { get; set; }

    // === ContentReport-specific fields (0 for ImpersonationAlert/ExamFailure) ===

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

    // === Common workflow fields ===

    /// <summary>
    /// Report status. Repository maps to domain enum.
    /// 0=Pending, 1=Reviewed, 2=Dismissed
    /// </summary>
    [Column("status")]
    public int Status { get; set; }

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
