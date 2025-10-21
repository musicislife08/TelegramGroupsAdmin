using TelegramGroupsAdmin.ContentDetection.Models;

namespace TelegramGroupsAdmin.ContentDetection.Repositories;

/// <summary>
/// Repository for managing file scan result caching
/// Provides hash-based lookups with 24-hour TTL
/// </summary>
public interface IFileScanResultRepository
{
    /// <summary>
    /// Get cached scan results for a file hash (24-hour TTL)
    /// Returns all scanner results for the hash that are still fresh
    /// </summary>
    /// <param name="fileHash">SHA256 hash of the file (64 hex chars)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of cached scan results within TTL (empty if no cache hits)</returns>
    Task<List<FileScanResultModel>> GetCachedResultsByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new scan result to the cache
    /// Used after performing a fresh scan
    /// </summary>
    /// <param name="scanResult">Scan result to cache</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created scan result with database-generated ID</returns>
    Task<FileScanResultModel> AddScanResultAsync(
        FileScanResultModel scanResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleanup expired scan results (older than 24 hours)
    /// Should be called periodically via TickerQ background job
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of records deleted</returns>
    Task<int> CleanupExpiredResultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear ALL cached scan results (for testing purposes)
    /// WARNING: This removes all cache entries regardless of age
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of records deleted</returns>
    Task<int> ClearAllCacheAsync(CancellationToken cancellationToken = default);
}
