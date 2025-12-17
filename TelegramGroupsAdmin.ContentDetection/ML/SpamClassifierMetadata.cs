namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Metadata for SDCA spam classifier model.
/// Persisted alongside model file for versioning and audit trail.
/// </summary>
public class SpamClassifierMetadata
{
    /// <summary>
    /// Minimum balanced spam ratio (20%) - below this indicates too few spam samples.
    /// </summary>
    public const double MinBalancedSpamRatio = 0.2;

    /// <summary>
    /// Maximum balanced spam ratio (80%) - above this indicates too few ham samples.
    /// </summary>
    public const double MaxBalancedSpamRatio = 0.8;

    /// <summary>
    /// Target spam ratio (30%) - optimal balance for SDCA training.
    /// </summary>
    public const double TargetSpamRatio = 0.3;

    /// <summary>
    /// When the model was trained.
    /// </summary>
    public DateTimeOffset TrainedAt { get; set; }

    /// <summary>
    /// Number of spam samples used for training.
    /// </summary>
    public int SpamSampleCount { get; set; }

    /// <summary>
    /// Number of ham samples used for training.
    /// </summary>
    public int HamSampleCount { get; set; }

    /// <summary>
    /// Total number of training samples (spam + ham).
    /// </summary>
    public int TotalSampleCount => SpamSampleCount + HamSampleCount;

    /// <summary>
    /// Spam ratio in training data (should be between MinBalancedSpamRatio and MaxBalancedSpamRatio for balance).
    /// </summary>
    public double SpamRatio => TotalSampleCount > 0
        ? (double)SpamSampleCount / TotalSampleCount
        : 0.0;

    /// <summary>
    /// SHA256 hash of the model file for integrity verification.
    /// </summary>
    public string ModelHash { get; set; } = string.Empty;

    /// <summary>
    /// Model file size in bytes.
    /// </summary>
    public long ModelSizeBytes { get; set; }

    /// <summary>
    /// ML.NET version used for training.
    /// </summary>
    public string MLNetVersion { get; set; } = string.Empty;

    /// <summary>
    /// Whether training data appears balanced (spam ratio between MinBalancedSpamRatio and MaxBalancedSpamRatio).
    /// </summary>
    public bool IsBalanced => SpamRatio >= MinBalancedSpamRatio && SpamRatio <= MaxBalancedSpamRatio;
}
