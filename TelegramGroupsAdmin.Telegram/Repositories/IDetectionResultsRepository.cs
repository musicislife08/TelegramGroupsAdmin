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
    /// Phase 4.20+: Supports optional translation data for non-English samples
    /// Returns the ID of the created detection_result
    /// </summary>
    Task<long> AddManualTrainingSampleAsync(
        string messageText,
        bool isSpam,
        string source,
        int? confidence,
        string? addedBy,
        string? translatedText = null,
        string? detectedLanguage = null,
        CancellationToken cancellationToken = default);

    // ====================================================================================
    // File Scanning UI Methods (Phase 4.22)
    // ====================================================================================

    /// <summary>
    /// Get file scan results for UI display (paginated)
    /// Filters by detection_source='file_scan' only
    /// </summary>
    /// <param name="limit">Maximum number of results to return</param>
    /// <param name="offset">Number of results to skip (for pagination)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of file scan detection results ordered by detected_at DESC</returns>
    Task<List<DetectionResultRecord>> GetFileScanResultsAsync(
        int limit = 50,
        int offset = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file scan statistics for UI dashboard (7-day window)
    /// Returns counts grouped by scanner (ClamAV, VirusTotal) for infected files only
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary keyed by detection_method (scanner name) with count of detections</returns>
    Task<Dictionary<string, int>> GetFileScanStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get total count of file scan results (for pagination)
    /// Filters by detection_source='file_scan' only
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total count of file scan results</returns>
    Task<int> GetFileScanResultsCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get analytics for messages flagged as spam by detection algorithms but vetoed (overridden) by OpenAI.
    /// Returns overall veto statistics and per-algorithm breakdown to identify overly aggressive checks.
    /// </summary>
    /// <param name="since">Start date for analytics period</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Analytics showing veto rates and per-algorithm statistics</returns>
    Task<OpenAIVetoAnalytics> GetOpenAIVetoAnalyticsAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent messages that were flagged as spam but vetoed by OpenAI.
    /// Used for manual inspection and algorithm tuning.
    /// </summary>
    /// <param name="limit">Maximum number of messages to return (default 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of vetoed messages with details about which checks flagged them</returns>
    Task<List<VetoedMessage>> GetRecentVetoedMessagesAsync(int limit = 50, CancellationToken cancellationToken = default);
}
