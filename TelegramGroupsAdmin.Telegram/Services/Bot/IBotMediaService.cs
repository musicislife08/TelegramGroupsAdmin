using Telegram.Bot.Types;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.Telegram.Services.Bot;

/// <summary>
/// Service layer for Telegram media/file operations.
/// Orchestrates IBotMediaHandler with caching for photos and file downloads.
/// Application code should use this, not IBotMediaHandler directly.
/// </summary>
public interface IBotMediaService
{
    /// <summary>
    /// Get or fetch user profile photo with smart cache invalidation.
    /// Returns relative web path and file_unique_id for storage.
    /// Uses file_unique_id to detect photo changes.
    /// </summary>
    /// <param name="userId">Telegram user ID</param>
    /// <param name="knownPhotoId">Known file_unique_id for cache invalidation (null for first fetch)</param>
    /// <param name="user">Optional user for logging context</param>
    /// <param name="ct">Cancellation token</param>
    Task<UserPhotoResult?> GetUserPhotoAsync(
        long userId,
        string? knownPhotoId = null,
        TelegramUser? user = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get or fetch chat icon (group/channel photo).
    /// Returns relative web path or null if not available.
    /// </summary>
    Task<string?> GetChatIconAsync(ChatIdentity chat, CancellationToken ct = default);

    /// <summary>
    /// Get file information for downloading.
    /// Returns file metadata including file_path for download.
    /// </summary>
    Task<TGFile> GetFileAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Download a file from Telegram servers to a stream.
    /// </summary>
    /// <param name="filePath">File path from GetFileAsync result</param>
    /// <param name="destination">Destination stream</param>
    /// <param name="ct">Cancellation token</param>
    Task DownloadFileAsync(string filePath, Stream destination, CancellationToken ct = default);

    /// <summary>
    /// Download a file by ID and return as byte array.
    /// Convenience method that combines GetFileAsync + DownloadFileAsync.
    /// </summary>
    Task<byte[]> DownloadFileAsBytesAsync(string fileId, CancellationToken ct = default);

    /// <summary>
    /// Get user profile photos.
    /// </summary>
    Task<UserProfilePhotos> GetUserProfilePhotosAsync(
        long userId,
        int offset = 0,
        int limit = 1,
        CancellationToken ct = default);
}
