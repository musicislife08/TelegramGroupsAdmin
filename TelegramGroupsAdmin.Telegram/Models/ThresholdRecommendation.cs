namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// UI model for ML-generated threshold optimization recommendations.
/// Represents a single recommendation for adjusting algorithm thresholds.
/// </summary>
public record ThresholdRecommendation
{
    public long Id { get; init; }

    /// <summary>
    /// Algorithm name (e.g., "Bayes", "StopWords", "Similarity")
    /// </summary>
    public required string AlgorithmName { get; init; }

    /// <summary>
    /// Current threshold value from spam_detection_config
    /// </summary>
    public decimal? CurrentThreshold { get; init; }

    /// <summary>
    /// ML-recommended threshold value
    /// </summary>
    public decimal RecommendedThreshold { get; init; }

    /// <summary>
    /// ML model confidence score (0-100)
    /// </summary>
    public decimal ConfidenceScore { get; init; }

    // Supporting Evidence

    /// <summary>
    /// Current veto rate for this algorithm (percentage)
    /// </summary>
    public decimal VetoRateBefore { get; init; }

    /// <summary>
    /// Estimated veto rate after applying recommendation (percentage)
    /// </summary>
    public decimal? EstimatedVetoRateAfter { get; init; }

    /// <summary>
    /// Sample message IDs that were vetoed by OpenAI (evidence for recommendation)
    /// </summary>
    public List<long> SampleVetoedMessageIds { get; init; } = [];

    /// <summary>
    /// Number of spam flags from this algorithm in training period
    /// </summary>
    public int SpamFlagsCount { get; init; }

    /// <summary>
    /// Number of vetoes from this algorithm in training period
    /// </summary>
    public int VetoedCount { get; init; }

    // Training Metadata

    /// <summary>
    /// Start of analysis period used for training
    /// </summary>
    public DateTimeOffset TrainingPeriodStart { get; init; }

    /// <summary>
    /// End of analysis period used for training
    /// </summary>
    public DateTimeOffset TrainingPeriodEnd { get; init; }

    /// <summary>
    /// When this recommendation was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    // Admin Action Tracking

    /// <summary>
    /// Status: pending, approved, rejected, applied
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// User ID who reviewed this recommendation
    /// </summary>
    public string? ReviewedByUserId { get; init; }

    /// <summary>
    /// Username of reviewer (for display)
    /// </summary>
    public string? ReviewedByUsername { get; init; }

    /// <summary>
    /// When the recommendation was reviewed
    /// </summary>
    public DateTimeOffset? ReviewedAt { get; init; }

    /// <summary>
    /// Admin notes explaining approval/rejection
    /// </summary>
    public string? ReviewNotes { get; init; }
}
