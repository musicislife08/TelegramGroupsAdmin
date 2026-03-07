namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Singleton service for Naive Bayes spam classification.
/// Thread-safe with immutable container pattern for atomic model swapping.
/// Follows the same architecture as <see cref="IMLTextClassifierService"/>.
/// </summary>
public interface IBayesClassifierService
{
    /// <summary>
    /// Trains a new Bayes classifier from the training data repository and atomically swaps it in.
    /// Uses SemaphoreSlim to prevent overlapping retraining.
    /// </summary>
    Task TrainAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Classifies a message using the current trained model.
    /// Thread-safe: takes a snapshot reference of the current model.
    /// Returns null if the classifier is not trained.
    /// </summary>
    BayesClassificationResult? Classify(string message);

    /// <summary>
    /// Gets metadata about the current trained model.
    /// Returns null if no model is loaded.
    /// </summary>
    BayesClassifierMetadata? GetMetadata();
}
