namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Performance timing statistics for a single spam detection algorithm.
/// ML-5: Per-algorithm execution time metrics from check_results_json JSONB column
/// </summary>
public class AlgorithmPerformanceStats
{
    /// <summary>
    /// Algorithm name (e.g., "Bayes", "StopWords", "OpenAI", etc.)
    /// </summary>
    public string CheckName { get; set; } = string.Empty;

    /// <summary>
    /// Total number of times this algorithm was executed
    /// </summary>
    public int TotalExecutions { get; set; }

    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    public double AverageMs { get; set; }

    /// <summary>
    /// 95th percentile execution time in milliseconds
    /// </summary>
    public double P95Ms { get; set; }

    /// <summary>
    /// Maximum execution time observed in milliseconds
    /// </summary>
    public double MaxMs { get; set; }

    /// <summary>
    /// Minimum execution time observed in milliseconds
    /// </summary>
    public double MinMs { get; set; }

    /// <summary>
    /// Total time contribution (average × frequency)
    /// Helps identify which algorithm consumes the most aggregate time
    /// </summary>
    public double TotalTimeContribution { get; set; }

    /// <summary>
    /// Performance rating based on average execution time
    /// Fast: &lt;100ms, Medium: 100-500ms, Slow: &gt;500ms
    /// </summary>
    public string PerformanceRating
    {
        get
        {
            if (AverageMs < 100) return "Fast";
            if (AverageMs < 500) return "Medium";
            return "Slow";
        }
    }
}
