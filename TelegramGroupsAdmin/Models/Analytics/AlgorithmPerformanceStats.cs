namespace TelegramGroupsAdmin.Models.Analytics;

/// <summary>
/// Performance timing statistics for a single spam detection algorithm.
/// ML-5: Per-algorithm execution time metrics from check_results_json JSONB column
/// </summary>
public record AlgorithmPerformanceStats
{
    /// <summary>
    /// Algorithm name (e.g., "Bayes", "StopWords", "OpenAI", etc.)
    /// </summary>
    public required string CheckName { get; init; }

    /// <summary>
    /// Total number of times this algorithm was executed
    /// </summary>
    public required int TotalExecutions { get; init; }

    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    public required double AverageMs { get; init; }

    /// <summary>
    /// 95th percentile execution time in milliseconds
    /// </summary>
    public required double P95Ms { get; init; }

    /// <summary>
    /// Maximum execution time observed in milliseconds
    /// </summary>
    public required double MaxMs { get; init; }

    /// <summary>
    /// Minimum execution time observed in milliseconds
    /// </summary>
    public required double MinMs { get; init; }

    /// <summary>
    /// Total time contribution (average × frequency)
    /// Helps identify which algorithm consumes the most aggregate time
    /// </summary>
    public required double TotalTimeContribution { get; init; }

    /// <summary>
    /// Performance rating based on average execution time
    /// Fast: &lt;100ms, Medium: 100-500ms, Slow: &gt;500ms
    /// </summary>
    public string PerformanceRating =>
        AverageMs switch
        {
            < 100 => "Fast",
            < 500 => "Medium",
            _ => "Slow"
        };
}
