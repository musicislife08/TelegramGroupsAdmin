namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Analytics for messages flagged as spam by detection algorithms but vetoed (overridden) by OpenAI.
/// Used to identify overly aggressive algorithms and quantify OpenAI's false positive prevention.
/// </summary>
public record OpenAIVetoAnalytics
{
    /// <summary>
    /// Total number of spam detections in the time period
    /// </summary>
    public int TotalDetections { get; init; }

    /// <summary>
    /// Number of detections where OpenAI vetoed (overrode) other spam flags
    /// </summary>
    public int VetoedCount { get; init; }

    /// <summary>
    /// Percentage of detections that were vetoed by OpenAI (VetoedCount / TotalDetections * 100)
    /// </summary>
    public decimal VetoRate { get; init; }

    /// <summary>
    /// Per-algorithm statistics showing how often each algorithm's spam flags are vetoed
    /// </summary>
    public List<AlgorithmVetoStats> AlgorithmStats { get; init; } = [];
}

/// <summary>
/// Statistics for a single spam detection algorithm's veto patterns.
/// High veto rates indicate the algorithm may be too aggressive and needs tuning.
/// </summary>
public record AlgorithmVetoStats
{
    /// <summary>
    /// Name of the detection algorithm (e.g., "StopWords", "Bayes", "TF-IDF Similarity")
    /// </summary>
    public required string AlgorithmName { get; init; }

    /// <summary>
    /// Total number of times this algorithm flagged messages as spam
    /// </summary>
    public int SpamFlagsCount { get; set; }

    /// <summary>
    /// Number of times OpenAI vetoed this algorithm's spam flag
    /// </summary>
    public int VetoedCount { get; init; }

    /// <summary>
    /// Percentage of this algorithm's spam flags that were vetoed (VetoedCount / SpamFlagsCount * 100)
    /// </summary>
    public decimal VetoRate { get; set; }
}

/// <summary>
/// Details about a specific message that was flagged as spam but vetoed by OpenAI.
/// Used for manual inspection and algorithm tuning.
/// </summary>
public record VetoedMessage
{
    /// <summary>
    /// ID of the message that was vetoed
    /// </summary>
    public long MessageId { get; init; }

    /// <summary>
    /// When the detection occurred
    /// </summary>
    public DateTimeOffset DetectedAt { get; init; }

    /// <summary>
    /// Truncated message text for display (first 100 characters)
    /// </summary>
    public string? MessagePreview { get; init; }

    /// <summary>
    /// Names of algorithms that flagged this message as spam
    /// </summary>
    public List<string> ContentCheckNames { get; init; } = [];

    /// <summary>
    /// OpenAI's confidence level when vetoing (0-100)
    /// </summary>
    public int OpenAIConfidence { get; init; }

    /// <summary>
    /// OpenAI's explanation for why the message is not spam
    /// </summary>
    public string? OpenAIReason { get; init; }
}
