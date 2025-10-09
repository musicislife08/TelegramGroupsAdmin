using TelegramGroupsAdmin.Models;

namespace TelegramGroupsAdmin.Repositories;

/// <summary>
/// Repository for managing spam detection results
/// </summary>
public interface IDetectionResultsRepository
{
    /// <summary>
    /// Insert a new detection result (spam or ham classification)
    /// </summary>
    Task InsertAsync(DetectionResultRecord result);

    /// <summary>
    /// Get detection result by ID
    /// </summary>
    Task<DetectionResultRecord?> GetByIdAsync(long id);

    /// <summary>
    /// Get all detection results for a specific message
    /// </summary>
    Task<List<DetectionResultRecord>> GetByMessageIdAsync(long messageId);

    /// <summary>
    /// Get recent detection results with limit
    /// </summary>
    Task<List<DetectionResultRecord>> GetRecentAsync(int limit = 100);

    /// <summary>
    /// Get spam training samples for Bayes classifier
    /// Bounded query: all manual samples + recent 10k auto samples
    /// </summary>
    Task<List<(string MessageText, bool IsSpam)>> GetTrainingSamplesAsync();

    /// <summary>
    /// Get spam samples for similarity check (TF-IDF)
    /// Returns only spam messages (is_spam=true)
    /// </summary>
    Task<List<string>> GetSpamSamplesForSimilarityAsync(int limit = 1000);

    /// <summary>
    /// Check if user is trusted (bypasses spam detection)
    /// Checks user_actions table for active 'trust' action
    /// </summary>
    Task<bool> IsUserTrustedAsync(long userId, long? chatId = null);

    /// <summary>
    /// Get detection statistics
    /// </summary>
    Task<DetectionStats> GetStatsAsync();

    /// <summary>
    /// Delete detection results older than specified date
    /// (Used for cleanup - though per CLAUDE.md, detection_results are permanent)
    /// </summary>
    Task<int> DeleteOlderThanAsync(long timestamp);
}
