namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Recommendation to ADD a new stop word based on spam corpus analysis
/// </summary>
public record StopWordAdditionRecommendation
{
    /// <summary>
    /// The word recommended to be added to stop words list
    /// </summary>
    public required string Word { get; init; }

    /// <summary>
    /// Frequency of this word in spam training samples (percentage, 0-100)
    /// </summary>
    public required decimal SpamFrequencyPercent { get; init; }

    /// <summary>
    /// Frequency of this word in legitimate messages (percentage, 0-100)
    /// </summary>
    public required decimal LegitFrequencyPercent { get; init; }

    /// <summary>
    /// Spam-to-legit ratio (higher = better candidate)
    /// Calculated as: spamFreq / (legitFreq + 1)
    /// </summary>
    public required decimal SpamToLegitRatio { get; init; }

    /// <summary>
    /// Number of spam training samples containing this word
    /// </summary>
    public required int SpamSampleCount { get; init; }

    /// <summary>
    /// Number of legitimate messages containing this word
    /// </summary>
    public required int LegitSampleCount { get; init; }

    /// <summary>
    /// Total number of spam training samples analyzed
    /// </summary>
    public required int TotalSpamSamples { get; init; }

    /// <summary>
    /// Total number of legitimate messages analyzed
    /// </summary>
    public required int TotalLegitMessages { get; init; }
}

/// <summary>
/// Recommendation to REMOVE an existing stop word based on precision analysis
/// </summary>
public record StopWordRemovalRecommendation
{
    /// <summary>
    /// The stop word recommended for removal
    /// </summary>
    public required string Word { get; init; }

    /// <summary>
    /// Precision of this stop word (percentage, 0-100)
    /// Calculated as: correctTriggers / (correctTriggers + falsePositives) * 100
    /// </summary>
    public required decimal PrecisionPercent { get; init; }

    /// <summary>
    /// Total number of times this stop word triggered detection
    /// </summary>
    public required int TotalTriggers { get; init; }

    /// <summary>
    /// Number of correct triggers (stop word triggered and message was spam)
    /// </summary>
    public required int CorrectTriggers { get; init; }

    /// <summary>
    /// Number of false positives (stop word triggered but message was ham)
    /// </summary>
    public required int FalsePositives { get; init; }

    /// <summary>
    /// When this stop word last triggered (null if never triggered)
    /// </summary>
    public DateTimeOffset? LastTriggeredAt { get; init; }

    /// <summary>
    /// Number of days since last trigger (null if never triggered)
    /// </summary>
    public int? DaysSinceLastTrigger { get; init; }

    /// <summary>
    /// Reason for removal recommendation
    /// </summary>
    public required string RemovalReason { get; init; }
}

/// <summary>
/// Performance-based stop word cleanup recommendation
/// Triggered when StopWords check average time exceeds threshold
/// </summary>
public record StopWordPerformanceCleanup
{
    /// <summary>
    /// The stop word recommended for removal to improve performance
    /// </summary>
    public required string Word { get; init; }

    /// <summary>
    /// Precision of this stop word (percentage, 0-100)
    /// </summary>
    public required decimal PrecisionPercent { get; init; }

    /// <summary>
    /// Number of false positives caused by this word
    /// </summary>
    public required int FalsePositives { get; init; }

    /// <summary>
    /// Total number of triggers
    /// </summary>
    public required int TotalTriggers { get; init; }

    /// <summary>
    /// Inefficiency score: (falsePositiveRate × executionCost) / (precision + 0.01)
    /// Higher = more inefficient
    /// </summary>
    public required decimal InefficientScore { get; init; }

    /// <summary>
    /// Estimated time savings if this word is removed (milliseconds)
    /// </summary>
    public required decimal EstimatedTimeSavingsMs { get; init; }
}

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
