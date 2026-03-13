namespace TelegramGroupsAdmin.ContentDetection.Models;

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
