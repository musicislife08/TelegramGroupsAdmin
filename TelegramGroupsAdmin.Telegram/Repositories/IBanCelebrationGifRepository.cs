using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Repositories;

/// <summary>
/// Repository for managing ban celebration GIFs stored locally in /data/media/ban-gifs/
/// </summary>
public interface IBanCelebrationGifRepository
{
    /// <summary>
    /// Gets all ban celebration GIFs ordered by creation date
    /// </summary>
    Task<List<BanCelebrationGif>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a random GIF from the library
    /// </summary>
    Task<BanCelebrationGif?> GetRandomAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets a specific GIF by ID
    /// </summary>
    Task<BanCelebrationGif?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Adds a new GIF from a file stream (uploaded via UI)
    /// Saves file to /data/media/ban-gifs/{id}.{ext}
    /// </summary>
    Task<BanCelebrationGif> AddFromFileAsync(Stream fileStream, string fileName, string? name, CancellationToken ct = default);

    /// <summary>
    /// Adds a new GIF from a URL (downloads and stores locally)
    /// </summary>
    Task<BanCelebrationGif> AddFromUrlAsync(string url, string? name, CancellationToken ct = default);

    /// <summary>
    /// Deletes a GIF record and removes the file from disk
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Updates the cached Telegram file_id after first upload
    /// </summary>
    Task UpdateFileIdAsync(int id, string fileId, CancellationToken ct = default);

    /// <summary>
    /// Clears the cached Telegram file_id (used when the cached ID becomes invalid/stale)
    /// </summary>
    Task ClearFileIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Updates the thumbnail path for a GIF
    /// </summary>
    Task UpdateThumbnailPathAsync(int id, string thumbnailPath, CancellationToken ct = default);

    /// <summary>
    /// Updates the perceptual hash for a GIF (used after duplicate check completes)
    /// </summary>
    Task UpdatePhotoHashAsync(int id, byte[] photoHash, CancellationToken ct = default);

    /// <summary>
    /// Finds a GIF with a similar perceptual hash (for duplicate detection)
    /// </summary>
    /// <param name="photoHash">The hash to compare against</param>
    /// <param name="maxHammingDistance">Maximum allowed bit differences (default 8 = 87.5% similarity)</param>
    /// <returns>The first similar GIF found, or null if no duplicates</returns>
    Task<BanCelebrationGif?> FindSimilarAsync(byte[] photoHash, int maxHammingDistance = 8, CancellationToken ct = default);

    /// <summary>
    /// Resolves a relative file path to the full path on disk
    /// </summary>
    string GetFullPath(string relativePath);

    /// <summary>
    /// Gets the count of GIFs in the library
    /// </summary>
    Task<int> GetCountAsync(CancellationToken ct = default);
}
