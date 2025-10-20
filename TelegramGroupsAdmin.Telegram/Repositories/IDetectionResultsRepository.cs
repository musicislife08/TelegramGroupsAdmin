using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing spam detection results
/// </summary>
public interface IDetectionResultsRepository
{
    /// <summary>
    /// Insert a new detection result (spam or ham classification)
    /// </summary>
    Task InsertAsync(DetectionResultRecord result, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detection result by ID
    /// </summary>
    Task<DetectionResultRecord?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all detection results for a specific message
    /// </summary>
    Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all detection results for multiple messages in a single batch query (avoids N+1 problem)
    /// Returns dictionary keyed by message_id for efficient lookup in UI components
    /// </summary>
    Task<Dictionary<long, List<DetectionResultRecord>>> GetDetectionHistoryBatchAsync(IEnumerable<long> messageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent detection results with limit
    /// </summary>
    Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get spam training samples for Bayes classifier
    /// Bounded query: all manual samples + recent 10k auto samples
    /// </summary>
    Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get spam samples for similarity check (TF-IDF)
    /// Returns only spam messages (is_spam=true)
    /// </summary>
    Task<List<string>> GetSpamSamplesForSimilarityAsync(int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user is trusted (bypasses spam detection)
    /// Checks user_actions table for active 'trust' action
    /// </summary>
    Task<bool> IsUserTrustedAsync(long userId, long? chatId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent non-spam detection results for a user (global, not per-chat)
    /// Used for auto-whitelisting after N consecutive non-spam messages
    /// </summary>
    Task<List<DetectionResultRecord>> GetRecentNonSpamResultsForUserAsync(long userId, int limit = 3, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get detection statistics
    /// </summary>
    Task<DetectionStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete detection results older than specified date
    /// (Used for cleanup - though per CLAUDE.md, detection_results are permanent)
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default);

    // ====================================================================================
    // Training Data Management Methods (for TrainingData.razor UI)
    // ====================================================================================

    /// <summary>
    /// Get all training data records (detection_results WHERE used_for_training = true)
    /// with JOIN to messages for full details
    /// </summary>
    Task<List<DetectionResultRecord>> GetAllTrainingDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get training data statistics (spam vs ham counts, sources breakdown)
    /// </summary>
    Task<TrainingDataStats> GetTrainingDataStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a detection result's spam classification and training flag
    /// Used when editing training samples
    /// </summary>
    Task UpdateDetectionResultAsync(long id, bool isSpam, bool usedForTraining, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a detection result (hard delete)
    /// Used when removing bad training samples
    /// </summary>
    Task DeleteDetectionResultAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a manual training sample (creates message with chat_id=0, user_id=0 + detection_result)
    /// Returns the ID of the created detection_result
    /// </summary>
    Task<long> AddManualTrainingSampleAsync(string messageText, bool isSpam, string source, int? confidence, string? addedBy, CancellationToken cancellationToken = default);
}
