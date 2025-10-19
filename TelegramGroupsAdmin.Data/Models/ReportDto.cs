using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

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
