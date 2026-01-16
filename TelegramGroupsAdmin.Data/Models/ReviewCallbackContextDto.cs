using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Database entity for review moderation callback button contexts.
/// Stores context data for DM action buttons, enabling short callback IDs.
/// Action is passed in callback data, not stored here.
/// Deleted after button is clicked or after expiry (7 days).
/// </summary>
/// <remarks>
/// Table still named report_callback_contexts for backward compatibility.
/// Will be renamed to review_callback_contexts in a future migration.
/// </remarks>
[Table("report_callback_contexts")]
public class ReviewCallbackContextDto
{
    /// <summary>
    /// Unique identifier for this callback context
    /// </summary>
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// ID of the review this context applies to (in unified reviews table)
    /// </summary>
    [Required]
    [Column("report_id")]
    public long ReviewId { get; set; }

    /// <summary>
    /// Type of review (Report, ImpersonationAlert, ExamFailure).
    /// Determines which action handler to use.
    /// </summary>
    [Required]
    [Column("review_type")]
    public ReviewType ReviewType { get; set; } = ReviewType.Report;

    /// <summary>
    /// Chat ID where the review subject occurred
    /// </summary>
    [Required]
    [Column("chat_id")]
    public long ChatId { get; set; }

    /// <summary>
    /// Telegram user ID of the user being reviewed (target for moderation)
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
