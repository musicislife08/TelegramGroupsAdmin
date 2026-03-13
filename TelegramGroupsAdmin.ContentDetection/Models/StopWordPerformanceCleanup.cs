namespace TelegramGroupsAdmin.ContentDetection.Models;

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
