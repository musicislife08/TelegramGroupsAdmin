using TelegramGroupsAdmin.ContentDetection.Constants;

namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Metadata for SDCA spam classifier model.
/// Persisted alongside model file for versioning and audit trail.
/// </summary>
public class SpamClassifierMetadata
{
    /// <summary>
    /// Minimum balanced spam ratio - below this indicates too few spam samples.
    /// </summary>
    public const double MinBalancedSpamRatio = MLConstants.MinBalancedSpamRatio;

    /// <summary>
    /// Maximum balanced spam ratio - above this indicates too few ham samples.
    /// </summary>
    public const double MaxBalancedSpamRatio = MLConstants.MaxBalancedSpamRatio;

    /// <summary>
    /// Target spam ratio - optimal balance for SDCA training.
    /// </summary>
    public const double TargetSpamRatio = MLConstants.TargetSpamRatio;

    /// <summary>
    /// Minimum samples required per class (spam and ham) for meaningful training.
    /// Below this threshold, the model lacks sufficient data for reliable classification.
    /// </summary>
    public const int MinimumSamplesPerClass = MLConstants.MinimumSamplesPerClass;

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
