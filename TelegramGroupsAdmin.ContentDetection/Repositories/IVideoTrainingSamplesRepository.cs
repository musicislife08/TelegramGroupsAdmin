namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for querying video training samples for spam detection (ML-6)
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
}
