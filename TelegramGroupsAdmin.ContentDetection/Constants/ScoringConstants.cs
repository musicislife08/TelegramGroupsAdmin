namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Constants for content detection scoring thresholds.
/// Defines score values for individual checks (0.0-5.0 points).
/// </summary>
public static class ScoringConstants
{
    /// <summary>
    /// Maximum score value (definitive threat)
    /// </summary>
    public const double MaxScore = 5.0;

    /// <summary>
    /// Minimum score value (no evidence of threat)
    /// </summary>
    public const double MinScore = 0.0;

    // Spacing check scores
    /// <summary>
    /// Score for spacing/formatting anomaly detection
    /// </summary>
    public const double ScoreFormattingAnomaly = 0.8;

    // CAS check scores
    /// <summary>
    /// Score for user banned by Combot Anti-Spam (CAS)
    /// </summary>
    public const double ScoreCasBanned = 5.0;

    // Stop words check scores
    /// <summary>
    /// Mild stop words detection - single match in long message
    /// </summary>
    public const double ScoreStopWordsMild = 0.5;

    /// <summary>
    /// Moderate stop words detection - 1-2 matches
    /// </summary>
    public const double ScoreStopWordsModerate = 1.0;

    /// <summary>
    /// Severe stop words detection - 3+ matches or short message with match
    /// </summary>
    public const double ScoreStopWordsSevere = 2.0;

    // Bayes classifier scores
    /// <summary>
    /// Bayes 99%+ probability of spam
    /// </summary>
    public const double ScoreBayes99 = 5.0;

    /// <summary>
    /// Bayes 95-99% probability of spam
    /// </summary>
    public const double ScoreBayes95 = 3.5;

    /// <summary>
    /// Bayes 80-95% probability of spam
    /// </summary>
    public const double ScoreBayes80 = 2.0;

    /// <summary>
    /// Bayes 70-80% probability of spam
    /// </summary>
    public const double ScoreBayes70 = 1.0;

    // Invisible chars check scores
    /// <summary>
    /// Score for invisible character detection (heuristics-based)
    /// </summary>
    public const double ScoreInvisibleChars = 1.5;

    // ML Similarity check thresholds (probability → score mapping)
    // Using float to match ML.NET prediction output type
    /// <summary>
    /// ML similarity probability threshold for 5.0 points
    /// </summary>
    public const float SimilarityThreshold95 = 0.95f;

    /// <summary>
    /// ML similarity probability threshold for 3.5 points
    /// </summary>
    public const float SimilarityThreshold85 = 0.85f;

    /// <summary>
    /// ML similarity probability threshold for 2.0 points
    /// </summary>
    public const float SimilarityThreshold70 = 0.70f;

    /// <summary>
    /// ML similarity probability threshold for 1.0 point
    /// </summary>
    public const float SimilarityThreshold60 = 0.60f;

    // Score values for similarity thresholds
    /// <summary>
    /// Score for ML similarity >= 95%
    /// </summary>
    public const double ScoreSimilarity95 = 5.0;

    /// <summary>
    /// Score for ML similarity >= 85%
    /// </summary>
    public const double ScoreSimilarity85 = 3.5;

    /// <summary>
    /// Score for ML similarity >= 70%
    /// </summary>
    public const double ScoreSimilarity70 = 2.0;

    /// <summary>
    /// Score for ML similarity >= 60%
    /// </summary>
    public const double ScoreSimilarity60 = 1.0;
}
