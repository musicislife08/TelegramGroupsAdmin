namespace TelegramGroupsAdmin.ContentDetection.ML;

/// <summary>
/// Interface for ML.NET SDCA text classifier for spam detection.
/// Enables dependency injection and unit testing via mocking.
/// </summary>
public interface IMLTextClassifierService
{
    /// <summary>
    /// Trains the SDCA model with TF-IDF features from the training data repository.
    /// Uses SemaphoreSlim to prevent overlapping retraining.
    /// </summary>
    Task TrainModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads model and metadata from disk with SHA256 hash verification.
    /// Returns true if successful, false if model doesn't exist or verification fails.
    /// </summary>
    Task<bool> LoadModelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Predicts spam probability for a message.
    /// Thread-safe: uses volatile container for atomic reads.
    /// Returns null if model is not loaded.
    /// </summary>
    SpamPrediction? Predict(string messageText);

    /// <summary>
    /// Gets current model metadata (training stats, timestamp, hash).
    /// Returns null if no model is loaded.
    /// </summary>
    SpamClassifierMetadata? GetMetadata();
}
