using TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.SpamDetection.Repositories;

/// <summary>
/// Repository for managing training samples used in Bayes classification
/// </summary>
public interface ITrainingSamplesRepository
{
    /// <summary>
    /// Get all training samples
    /// </summary>
    Task<IEnumerable<TrainingSample>> GetAllSamplesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get only spam training samples (is_spam = true), optionally filtered by chat
    /// Used by Similarity spam check for TF-IDF matching
    /// </summary>
    Task<IEnumerable<TrainingSample>> GetSpamSamplesAsync(string? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment detection count for a sample when it successfully detects spam/ham
    /// </summary>
    Task IncrementDetectionCountAsync(long sampleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new training sample
    /// </summary>
    Task<long> AddSampleAsync(string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, string? chatId = null, string? addedBy = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get training samples by source
    /// </summary>
    Task<IEnumerable<TrainingSample>> GetSamplesBySourceAsync(string source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete training samples older than specified date
    /// </summary>
    Task<int> DeleteOldSamplesAsync(long olderThanUnixTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a training sample
    /// </summary>
    Task<bool> UpdateSampleAsync(long id, string messageText, bool isSpam, string source, int? confidenceWhenAdded = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific training sample
    /// </summary>
    Task<bool> DeleteSampleAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get training statistics
    /// </summary>
    Task<TrainingStats> GetStatsAsync(CancellationToken cancellationToken = default);
}

// Models are defined in TelegramGroupsAdmin.Data.Models namespace