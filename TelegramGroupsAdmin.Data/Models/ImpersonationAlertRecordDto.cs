using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// EF Core entity for impersonation_alerts table
/// Tracks impersonation detection events for manual review
/// </summary>
[Table("impersonation_alerts")]
public class ImpersonationAlertRecordDto
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("suspected_user_id")]
    public long SuspectedUserId { get; set; }

    [Column("target_user_id")]
    public long TargetUserId { get; set; }

    [Column("chat_id")]
    public long ChatId { get; set; }

    // Composite scoring
    [Column("total_score")]
    public int TotalScore { get; set; }

    [Column("risk_level")]
    public ImpersonationRiskLevel RiskLevel { get; set; }

    // Individual match details
    [Column("name_match")]
    public bool NameMatch { get; set; }

    [Column("photo_match")]
    public bool PhotoMatch { get; set; }

    [Column("photo_similarity_score")]
    public double? PhotoSimilarityScore { get; set; }

    // Review workflow
    [Column("detected_at")]
    public DateTimeOffset DetectedAt { get; set; }

    [Column("auto_banned")]
    public bool AutoBanned { get; set; }

    [Column("reviewed_by_user_id")]
    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    [Column("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    [Column("verdict")]
    public ImpersonationVerdict? Verdict { get; set; }

    // Navigation properties
    [ForeignKey(nameof(SuspectedUserId))]
    public virtual TelegramUserDto? SuspectedUser { get; set; }

    [ForeignKey(nameof(TargetUserId))]
    public virtual TelegramUserDto? TargetUser { get; set; }

    [ForeignKey(nameof(ReviewedByUserId))]
    public virtual UserRecordDto? ReviewedBy { get; set; }
}
