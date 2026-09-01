using TelegramGroupsAdmin.ContentDetection.Models;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing image training samples (ML-5)
/// Consolidated read/write operations for spam detection training data
/// </summary>
public interface IImageTrainingSamplesRepository
{
    /// <summary>
    /// Get recent image training samples with their photo hashes.
    /// Samples with a NULL hash (source image gone, hash not recomputed after the
    /// v1 to v2 hash migration) are excluded. Returns samples ordered by most
    /// recent first.
    /// </summary>
    /// <param name="limit">Maximum number of samples to return (for performance)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of usable training samples</returns>
    Task<List<ImageTrainingSample>> GetRecentSamplesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Save an image training sample from a labeled message
    /// Computes photo hash and stores with spam/ham label
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="chatId">Chat ID the message belongs to</param>
    /// <param name="isSpam">True = spam sample, False = ham (legitimate) sample</param>
    /// <param name="markedBy">Actor who labeled the message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sample was saved, False if message has no photo or hash computation failed</returns>
    Task<bool> SaveTrainingSampleAsync(
        int messageId,
        long chatId,
        bool isSpam,
        Actor markedBy,
        CancellationToken cancellationToken = default);
}
