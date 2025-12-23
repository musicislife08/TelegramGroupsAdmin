namespace TelegramGroupsAdmin.ContentDetection.Constants;

/// <summary>
/// Constants for machine learning operations.
/// </summary>
public static class MLConstants
{
    /// <summary>
    /// ML.NET random seed for reproducible training results
    /// </summary>
    public const int MlNetSeed = 42;

    /// <summary>
    /// Minimum balanced spam ratio - dataset must have at least 20% spam samples
    /// </summary>
    public const double MinBalancedSpamRatio = 0.2;

    /// <summary>
    /// Maximum balanced spam ratio - dataset must have at most 80% spam samples
    /// </summary>
    public const double MaxBalancedSpamRatio = 0.8;

    /// <summary>
    /// Target spam ratio for balanced training dataset (30% spam)
    /// </summary>
    public const double TargetSpamRatio = 0.3;

    /// <summary>
    /// Minimum number of samples required per class (spam/ham) for training
    /// </summary>
    public const int MinimumSamplesPerClass = 20;

    /// <summary>
    /// Minimum text length in characters for ML training samples
    /// </summary>
    public const int MinTextLength = 10;

    /// <summary>
    /// Ham multiplier to maintain ~30% spam ratio in training data
    /// </summary>
    public const double HamMultiplier = 2.33;

    // Stop word recommendation thresholds
    /// <summary>
    /// Minimum spam frequency percentage - word must appear in ≥5% of spam samples
    /// </summary>
    public const decimal MinimumSpamFrequencyPercent = 5.0m;

    /// <summary>
    /// Maximum legit frequency percentage - word must appear in &lt;1% of legit messages
    /// </summary>
    public const decimal MaximumLegitFrequencyPercent = 1.0m;

    /// <summary>
    /// Minimum number of spam samples needed for stop word recommendations
    /// </summary>
    public const int MinimumSpamSamples = 50;

    /// <summary>
    /// Minimum number of legit messages needed for stop word recommendations
    /// </summary>
    public const int MinimumLegitMessages = 100;

    /// <summary>
    /// Minimum precision percentage - recommend removal if precision &lt;70%
    /// </summary>
    public const decimal MinimumPrecisionPercent = 70.0m;

    /// <summary>
    /// Minimum number of triggers needed for reliable stop word statistics
    /// </summary>
    public const int MinimumTriggers = 5;

    /// <summary>
    /// Days considered inactive for stop word cleanup (30 days)
    /// </summary>
    public const int DaysConsideredInactive = 30;

    /// <summary>
    /// Performance threshold in milliseconds - trigger cleanup if avg time &gt;200ms
    /// </summary>
    public const decimal PerformanceThresholdMs = 200.0m;
}
