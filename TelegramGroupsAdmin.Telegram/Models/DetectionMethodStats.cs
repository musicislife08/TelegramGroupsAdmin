namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Effectiveness statistics for a single spam detection algorithm.
/// Phase 5: Analytics for individual algorithm performance (parsed from check_results_json)
/// </summary>
public class DetectionMethodStats
{
    /// <summary>
    /// Algorithm name (e.g., "StopWords", "CAS", "OpenAI", etc.)
    /// </summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>
    /// Total checks performed by this algorithm
    /// </summary>
    public int TotalChecks { get; set; }

    /// <summary>
    /// Number of times this algorithm individually voted "spam"
    /// </summary>
    public int SpamDetected { get; set; }

    /// <summary>
    /// Hit rate: % of checks where this algorithm voted spam (0-100)
    /// </summary>
    public double SpamPercentage { get; set; }

    /// <summary>
    /// Average confidence when voting spam (null for binary checks like CAS)
    /// Binary checks always return 0 or 100, so avg is meaningless
    /// </summary>
    public double? AverageSpamConfidence { get; set; }

    /// <summary>
    /// How many system false positives had this algorithm voting "spam"
    /// Shows if algorithm is too aggressive
    /// </summary>
    public int ContributedToFalsePositives { get; set; }

    /// <summary>
    /// How many system false negatives had this algorithm voting "clean"
    /// Shows if algorithm is missing spam
    /// </summary>
    public int ContributedToFalseNegatives { get; set; }
}
