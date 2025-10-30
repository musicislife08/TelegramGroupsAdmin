namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Simple DTO for threshold recommendations (no dependencies on Telegram layer).
/// Used by ML service to return recommendations that can be persisted by the caller.
/// </summary>
public record ThresholdRecommendationDto
{
    public required string AlgorithmName { get; init; }
    public decimal? CurrentThreshold { get; init; }
    public decimal RecommendedThreshold { get; init; }
    public decimal ConfidenceScore { get; init; }
    public decimal VetoRateBefore { get; init; }
    public decimal? EstimatedVetoRateAfter { get; init; }
    public List<long> SampleVetoedMessageIds { get; init; } = [];
    public int SpamFlagsCount { get; init; }
    public int VetoedCount { get; init; }
    public DateTimeOffset TrainingPeriodStart { get; init; }
    public DateTimeOffset TrainingPeriodEnd { get; init; }
}
