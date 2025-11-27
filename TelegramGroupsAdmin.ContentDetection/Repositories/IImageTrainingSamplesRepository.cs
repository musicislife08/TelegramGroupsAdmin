using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing image training samples (ML-5)
/// Consolidated read/write operations for spam detection training data
/// </summary>
public interface IImageTrainingSamplesRepository
{
    /// <summary>
    /// Get recent image training samples with their photo hashes
    /// Returns samples ordered by most recent first
    /// </summary>
    /// <param name="limit">Maximum number of samples to return (for performance)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of (PhotoHash, IsSpam) tuples</returns>
    Task<List<(byte[] PhotoHash, bool IsSpam)>> GetRecentSamplesAsync(
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save an image training sample from a labeled message
    /// Computes photo hash and stores with spam/ham label
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="isSpam">True = spam sample, False = ham (legitimate) sample</param>
    /// <param name="markedBy">Actor who labeled the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sample was saved, False if message has no photo or hash computation failed</returns>
    Task<bool> SaveTrainingSampleAsync(
        long messageId,
        bool isSpam,
        Actor markedBy,
        CancellationToken cancellationToken = default);
}
