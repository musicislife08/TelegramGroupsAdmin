using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for retrieving ML training data from multiple sources.
/// Encapsulates complex queries across training_labels, detection_results, messages, and translations.
/// </summary>
public interface IMLTrainingDataRepository
{
    /// <summary>
    /// Gets all spam training samples (explicit labels + high-confidence auto-detected).
    /// Explicit labels from training_labels override auto-detection.
    /// Uses NOT EXISTS correlated subqueries with composite (MessageId, ChatId) to prevent cross-chat data leakage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of spam samples with metadata</returns>
    Task<List<TrainingSample>> GetSpamSamplesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ham training samples (explicit labels + quality-filtered never-flagged messages).
    /// Dynamically caps implicit ham based on spam count to maintain balanced ratio (~30% spam).
    /// Uses NOT EXISTS correlated subqueries with composite (MessageId, ChatId) to prevent cross-chat data leakage.
    /// </summary>
    /// <param name="spamCount">Number of spam samples (for dynamic balancing)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of ham samples with metadata</returns>
    Task<List<TrainingSample>> GetHamSamplesAsync(int spamCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets training data balance statistics for UI display.
    /// Calculates spam/ham counts using same logic as actual training.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Balance statistics including spam count, explicit/implicit ham counts, and ratios</returns>
    Task<TrainingBalanceStats> GetTrainingBalanceStatsAsync(CancellationToken cancellationToken = default);
}
