using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Database entity for report moderation callback button contexts.
/// Stores context data for DM action buttons, enabling short callback IDs.
/// Action is passed in callback data, not stored here.
/// Deleted after button is clicked or after expiry (7 days).
/// </summary>
[Table("report_callback_contexts")]
public class ReportCallbackContextDto
{
    /// <summary>
    /// Unique identifier for this callback context
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// ID of the report this context applies to (in unified reports table)
    /// </summary>
    [Required]
    [Column("report_id")]
    public long ReportId { get; set; }

    /// <summary>
    /// Type of report. Repository maps to domain enum.
    /// 0=ContentReport, 1=ImpersonationAlert, 2=ExamFailure
    /// </summary>
    [Required]
    [Column("report_type")]
    public short ReportType { get; set; }

    /// <summary>
    /// Chat ID where the review subject occurred
    /// </summary>
    [Required]
    [Column("chat_id")]
    public long ChatId { get; set; }

    /// <summary>
    /// Telegram user ID of the user being reported (target for moderation)
    /// </summary>
    [Required]
    [Column("user_id")]
    public long UserId { get; set; }

    /// <summary>
    /// When this callback context was created
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
