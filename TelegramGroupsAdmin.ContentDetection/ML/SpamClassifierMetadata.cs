namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Metadata for SDCA spam classifier model.
/// Persisted alongside model file for versioning and audit trail.
/// </summary>
public class SpamClassifierMetadata
{
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
    /// Spam ratio in training data (should be between 0.2 and 0.8 for balance).
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
    /// Whether training data appears balanced (spam ratio between 0.2 and 0.8).
    /// </summary>
    public bool IsBalanced => SpamRatio >= 0.2 && SpamRatio <= 0.8;
}
