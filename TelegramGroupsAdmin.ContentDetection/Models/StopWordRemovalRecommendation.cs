namespace TelegramGroupsAdmin.ContentDetection.Models;

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
