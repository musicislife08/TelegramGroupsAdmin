using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelegramGroupsAdmin.Data.Models;

/// <summary>
/// Database model for ML-generated threshold optimization recommendations.
/// Stored recommendations for admin review before applying to spam detection config.
/// </summary>
[Table("threshold_recommendations")]
public class ThresholdRecommendationDto
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    /// <summary>
    /// Algorithm name (e.g., "Bayes", "StopWords", "Similarity")
    /// </summary>
    [Required]
    [Column("algorithm_name")]
    [MaxLength(100)]
    public string AlgorithmName { get; set; } = string.Empty;

    /// <summary>
    /// Current threshold value from spam_detection_config
    /// </summary>
    [Column("current_threshold")]
    public decimal? CurrentThreshold { get; set; }

    /// <summary>
    /// ML-recommended threshold value
    /// </summary>
    [Required]
    [Column("recommended_threshold")]
    public decimal RecommendedThreshold { get; set; }

    /// <summary>
    /// ML model confidence score (0-100)
    /// </summary>
    [Required]
    [Column("confidence_score")]
    public decimal ConfidenceScore { get; set; }

    // Supporting Evidence

    /// <summary>
    /// Current veto rate for this algorithm (percentage)
    /// </summary>
    [Required]
    [Column("veto_rate_before")]
    public decimal VetoRateBefore { get; set; }

    /// <summary>
    /// Estimated veto rate after applying recommendation (percentage)
    /// </summary>
    [Column("estimated_veto_rate_after")]
    public decimal? EstimatedVetoRateAfter { get; set; }

    /// <summary>
    /// Sample message IDs that were vetoed by OpenAI (evidence for recommendation)
    /// </summary>
    [Column("sample_vetoed_message_ids")]
    public long[]? SampleVetoedMessageIds { get; set; }

    /// <summary>
    /// Number of spam flags from this algorithm in training period
    /// </summary>
    [Column("spam_flags_count")]
    public int SpamFlagsCount { get; set; }

    /// <summary>
    /// Number of vetoes from this algorithm in training period
    /// </summary>
    [Column("vetoed_count")]
    public int VetoedCount { get; set; }

    // Training Metadata

    /// <summary>
    /// Start of analysis period used for training
    /// </summary>
    [Required]
    [Column("training_period_start")]
    public DateTimeOffset TrainingPeriodStart { get; set; }

    /// <summary>
    /// End of analysis period used for training
    /// </summary>
    [Required]
    [Column("training_period_end")]
    public DateTimeOffset TrainingPeriodEnd { get; set; }

    /// <summary>
    /// When this recommendation was created
    /// </summary>
    [Required]
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Admin Action Tracking

    /// <summary>
    /// Status: pending, approved, rejected, applied
    /// </summary>
    [Required]
    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// User who reviewed this recommendation (ASP.NET Identity user ID)
    /// </summary>
    [Column("reviewed_by_user_id")]
    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    /// <summary>
    /// When the recommendation was reviewed
    /// </summary>
    [Column("reviewed_at")]
    public DateTimeOffset? ReviewedAt { get; set; }

    /// <summary>
    /// Admin notes explaining approval/rejection
    /// </summary>
    [Column("review_notes")]
    public string? ReviewNotes { get; set; }

    // Navigation Properties

    /// <summary>
    /// User who reviewed this recommendation
    /// </summary>
    [ForeignKey(nameof(ReviewedByUserId))]
    public UserRecordDto? ReviewedByUser { get; set; }
}
