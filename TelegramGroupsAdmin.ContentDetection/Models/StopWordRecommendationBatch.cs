namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Complete batch of stop word recommendations
/// </summary>
public record StopWordRecommendationBatch
{
    /// <summary>
    /// Words recommended to be added to stop words list
    /// Sorted by SpamToLegitRatio descending (best candidates first)
    /// </summary>
    public required List<StopWordAdditionRecommendation> AdditionRecommendations { get; init; }

    /// <summary>
    /// Words recommended to be removed from stop words list
    /// Sorted by PrecisionPercent ascending (worst performers first)
    /// </summary>
    public required List<StopWordRemovalRecommendation> RemovalRecommendations { get; init; }

    /// <summary>
    /// Performance-based cleanup recommendations (only present if StopWords check is slow)
    /// Sorted by InefficientScore descending (most inefficient first)
    /// </summary>
    public required List<StopWordPerformanceCleanup> PerformanceCleanupRecommendations { get; init; }

    /// <summary>
    /// Start of analysis period
    /// </summary>
    public required DateTimeOffset AnalysisPeriodStart { get; init; }

    /// <summary>
    /// End of analysis period
    /// </summary>
    public required DateTimeOffset AnalysisPeriodEnd { get; init; }

    /// <summary>
    /// When this recommendation batch was generated
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Current average StopWords check execution time (milliseconds)
    /// Used to determine if performance cleanup is needed
    /// </summary>
    public decimal? CurrentAverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Performance threshold (milliseconds) - cleanup triggered if exceeded
    /// </summary>
    public decimal PerformanceThresholdMs { get; init; } = 200m;

    /// <summary>
    /// Whether performance cleanup is recommended
    /// </summary>
    public bool IsPerformanceCleanupRecommended =>
        CurrentAverageExecutionTimeMs.HasValue &&
        CurrentAverageExecutionTimeMs.Value > PerformanceThresholdMs;

    /// <summary>
    /// Total number of spam training samples analyzed
    /// </summary>
    public required int TotalSpamSamples { get; init; }

    /// <summary>
    /// Total number of legitimate messages analyzed
    /// </summary>
    public required int TotalLegitMessages { get; init; }

    /// <summary>
    /// Total number of detection results analyzed
    /// </summary>
    public required int TotalDetectionResults { get; init; }

    /// <summary>
    /// Validation message if insufficient data for analysis
    /// </summary>
    public string? ValidationMessage { get; init; }

    /// <summary>
    /// Whether this batch has valid recommendations
    /// </summary>
    public bool HasRecommendations =>
        AdditionRecommendations.Count > 0 ||
        RemovalRecommendations.Count > 0 ||
        PerformanceCleanupRecommendations.Count > 0;
}
