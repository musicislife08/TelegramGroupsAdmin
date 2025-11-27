using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing video training samples (ML-6)
/// Consolidated read/write operations for spam detection training data
/// </summary>
public interface IVideoTrainingSamplesRepository
{
    /// <summary>
    /// Get recent video training samples with their keyframe hashes
    /// Returns samples ordered by most recent first
    /// </summary>
    /// <param name="limit">Maximum number of samples to return (for performance)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (KeyframeHashes JSON, IsSpam) tuples</returns>
    Task<List<(string KeyframeHashes, bool IsSpam)>> GetRecentSamplesAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save a video training sample from a labeled message
    /// Extracts keyframes, computes perceptual hashes, and stores with spam/ham label
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="isSpam">True = spam sample, False = ham (legitimate) sample</param>
    /// <param name="markedBy">Actor who labeled the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sample was saved, False if message has no video or hash computation failed</returns>
    Task<bool> SaveTrainingSampleAsync(
        long messageId,
        bool isSpam,
        Actor markedBy,
        CancellationToken cancellationToken = default);
}
