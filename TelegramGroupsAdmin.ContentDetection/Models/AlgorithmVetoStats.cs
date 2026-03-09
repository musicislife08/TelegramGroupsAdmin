namespace TelegramGroupsAdmin.ContentDetection.Models;

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
