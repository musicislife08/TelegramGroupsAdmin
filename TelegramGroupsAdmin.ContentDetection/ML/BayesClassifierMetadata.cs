namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Metadata for the Bayes classifier model state.
/// Unlike ML.NET, Bayes doesn't persist to disk — it retrains on startup.
/// </summary>
public sealed record BayesClassifierMetadata
{
    /// <summary>
    /// When the classifier was last trained.
    /// </summary>
    public DateTimeOffset TrainedAt { get; init; }

    /// <summary>
    /// Number of spam samples used for training.
    /// </summary>
    public int SpamSampleCount { get; init; }

    /// <summary>
    /// Number of ham samples used for training.
    /// </summary>
    public int HamSampleCount { get; init; }

    /// <summary>
    /// Total number of training samples (spam + ham).
    /// </summary>
    public int TotalSampleCount => SpamSampleCount + HamSampleCount;

    /// <summary>
    /// Spam ratio in training data.
    /// </summary>
    public double SpamRatio => TotalSampleCount > 0
        ? (double)SpamSampleCount / TotalSampleCount
        : 0.0;

    /// <summary>
    /// Number of unique words in the spam vocabulary.
    /// </summary>
    public int SpamVocabularySize { get; init; }

    /// <summary>
    /// Number of unique words in the ham vocabulary.
    /// </summary>
    public int HamVocabularySize { get; init; }
}
