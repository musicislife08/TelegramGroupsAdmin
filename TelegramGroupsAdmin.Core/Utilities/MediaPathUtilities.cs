namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Utilities for constructing media file paths
/// Phase 4.X: Media attachments (Animation, Video, Audio, Voice, Sticker, VideoNote, Document)
/// </summary>
public static class MediaPathUtilities
{
    /// <summary>
    /// Get the subdirectory name for a given media type
    /// </summary>
    /// <param name="mediaType">The media type enum value (cast from int)</param>
    /// <returns>Subdirectory name (e.g., "video", "audio", "sticker")</returns>
    public static string GetMediaSubdirectory(int mediaType)
    {
        // MediaType enum values:
        // None = 0, Animation = 1, Video = 2, Audio = 3, Voice = 4, Sticker = 5, VideoNote = 6, Document = 7
        return mediaType switch
        {
            1 => "video",    // Animation
            2 => "video",    // Video
            6 => "video",    // VideoNote
            3 => "audio",    // Audio
            4 => "audio",    // Voice
            5 => "sticker",  // Sticker
            7 => "document", // Document
            _ => "other"     // None or unknown
        };
    }

    /// <summary>
    /// Construct the full relative path for storing a media file
    /// </summary>
    /// <param name="filename">The filename (e.g., "animation_123_ABC.mp4")</param>
    /// <param name="mediaType">The media type enum value (cast from int)</param>
    /// <returns>Relative path (e.g., "media/video/animation_123_ABC.mp4")</returns>
    public static string GetMediaStoragePath(string filename, int mediaType)
    {
        var subDir = GetMediaSubdirectory(mediaType);
        return $"media/{subDir}/{filename}";
    }

    /// <summary>
    /// Construct the web URL path for serving a media file
    /// </summary>
    /// <param name="filename">The filename (e.g., "animation_123_ABC.mp4")</param>
    /// <param name="mediaType">The media type enum value (cast from int)</param>
    /// <returns>Web path (e.g., "/media/video/animation_123_ABC.mp4")</returns>
    public static string GetMediaWebPath(string filename, int mediaType)
    {
        var subDir = GetMediaSubdirectory(mediaType);
        return $"/media/{subDir}/{filename}";
    }

    /// <summary>
    /// Resolve a relative path to an absolute path for disk operations
    /// </summary>
    /// <param name="relativePath">Relative path (e.g., "user_photos/123.jpg")</param>
    /// <param name="basePath">Base storage path (e.g., "/data")</param>
    /// <returns>Absolute path (e.g., "/data/media/user_photos/123.jpg")</returns>
    public static string ToAbsolutePath(string relativePath, string basePath)
        => Path.Combine(basePath, "media", relativePath);

    /// <summary>
    /// Convert a relative photo path to a web URL path.
    /// Photos use a separate storage pattern from media attachments:
    /// - Storage: /data/media/{relativePath} (e.g., /data/media/full/{chatId}/{messageId}.jpg)
    /// - Web URL: /media/{relativePath} (e.g., /media/full/{chatId}/{messageId}.jpg)
    /// Works for both PhotoLocalPath and PhotoThumbnailPath (both stored as relative paths).
    /// </summary>
    /// <param name="relativePath">Relative photo path (e.g., "full/{chatId}/{messageId}.jpg")</param>
    /// <returns>Web URL path (e.g., "/media/full/{chatId}/{messageId}.jpg")</returns>
    public static string? GetPhotoWebPath(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return null;

        return $"/media/{relativePath}";
    }

    /// <summary>
    /// Validates that a media file exists on the filesystem.
    /// REFACTOR-3: Extracted from MessageHistoryRepository and MessageQueryService (DRY).
    /// </summary>
    /// <param name="mediaLocalPath">The stored media filename (e.g., "animation_123_ABC.mp4")</param>
    /// <param name="mediaType">The media type enum value (nullable)</param>
    /// <param name="imageStoragePath">Base storage path (e.g., "/data")</param>
    /// <param name="fullPathOut">Output: the full filesystem path that was checked (null if no validation performed)</param>
    /// <returns>
    /// <list type="bullet">
    /// <item><description>Returns <paramref name="mediaLocalPath"/> unchanged if null/empty (passthrough, no validation needed)</description></item>
    /// <item><description>Returns <paramref name="mediaLocalPath"/> unchanged if <paramref name="mediaType"/> is null (passthrough, can't construct path)</description></item>
    /// <item><description>Returns <paramref name="mediaLocalPath"/> if file exists on disk (validation passed)</description></item>
    /// <item><description>Returns null if file does not exist (validation failed - caller should clear the path)</description></item>
    /// </list>
    /// </returns>
    public static string? ValidateMediaPath(
        string? mediaLocalPath,
        int? mediaType,
        string imageStoragePath,
        out string? fullPathOut)
    {
        fullPathOut = null;

        // Skip if no media path set or no media type
        if (string.IsNullOrEmpty(mediaLocalPath) || !mediaType.HasValue)
            return mediaLocalPath;

        // Construct full path (e.g., /data/media/video/animation_123_ABC.mp4)
        var relativePath = GetMediaStoragePath(mediaLocalPath, mediaType.Value);
        fullPathOut = Path.Combine(imageStoragePath, relativePath);

        // Return path if file exists, null otherwise
        return File.Exists(fullPathOut) ? mediaLocalPath : null;
    }
}
