using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for retrieving ML training data from multiple sources.
/// Encapsulates complex queries across training_labels, detection_results, messages, and translations.
/// </summary>
public interface IMLTrainingDataRepository
{
    /// <summary>
    /// Gets all labeled message IDs from training_labels table.
    /// Used to avoid duplicate queries between GetSpamSamplesAsync and GetHamSamplesAsync.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HashSet of all labeled message IDs</returns>
    Task<HashSet<long>> GetLabeledMessageIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all spam training samples (explicit labels + high-confidence auto-detected).
    /// Explicit labels from training_labels override auto-detection.
    /// </summary>
    /// <param name="labeledMessageIds">Already-labeled message IDs to exclude from implicit spam (avoids duplicate query)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of spam samples with metadata</returns>
    Task<List<TrainingSample>> GetSpamSamplesAsync(HashSet<long> labeledMessageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ham training samples (explicit labels + quality-filtered never-flagged messages).
    /// Dynamically caps implicit ham based on spam count to maintain balanced ratio (~30% spam).
    /// </summary>
    /// <param name="spamCount">Number of spam samples (for dynamic balancing)</param>
    /// <param name="labeledMessageIds">Already-labeled message IDs to exclude from implicit ham (avoids duplicate query)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of ham samples with metadata</returns>
    Task<List<TrainingSample>> GetHamSamplesAsync(int spamCount, HashSet<long> labeledMessageIds, CancellationToken cancellationToken = default);
}
