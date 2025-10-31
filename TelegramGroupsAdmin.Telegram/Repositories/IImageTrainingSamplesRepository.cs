using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing image training samples (ML-5)
/// </summary>
public interface IImageTrainingSamplesRepository
{
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
