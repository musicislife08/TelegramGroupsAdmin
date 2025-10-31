namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for querying image training samples for spam detection (ML-5)
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
}
