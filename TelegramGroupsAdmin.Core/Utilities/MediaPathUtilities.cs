namespace TelegramGroupsAdmin.Core.Utilities;

/// <summary>
/// Utilities for constructing media file paths and detecting media formats.
/// Phase 4.X: Media attachments (Animation, Video, Audio, Voice, Sticker, VideoNote, Document)
/// </summary>
public static class MediaPathUtilities
{
    /// <summary>
    /// Detects whether a file contains video content by examining its magic bytes (file signature),
    /// regardless of file extension. Giphy and other services often serve MP4/WebM video content
    /// from URLs with .gif extensions.
    /// </summary>
    /// <param name="filePath">Full path to the file to inspect</param>
    /// <returns>True if the file's magic bytes match a known video format (MP4/MOV/M4V, WebM/MKV, AVI)</returns>
    public static bool IsVideoContent(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[12];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var bytesRead = fs.Read(header);
            if (bytesRead < 4) return false;

            // MP4/MOV/M4V: "ftyp" at offset 4
            if (bytesRead >= 8
                && header[4] == (byte)'f' && header[5] == (byte)'t'
                && header[6] == (byte)'y' && header[7] == (byte)'p')
                return true;

            // WebM/MKV: EBML header 0x1A45DFA3
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
                return true;

            // AVI: "RIFF....AVI "
            if (bytesRead >= 12
                && header[0] == (byte)'R' && header[1] == (byte)'I'
                && header[2] == (byte)'F' && header[3] == (byte)'F'
                && header[8] == (byte)'A' && header[9] == (byte)'V'
                && header[10] == (byte)'I' && header[11] == (byte)' ')
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Get the subdirectory name for a given media type
    /// </summary>
    /// <param name="mediaType">The media type enum value (cast from int)</param>
    /// <returns>Subdirectory name (e.g., "video", "audio", "sticker")</returns>
    public static string GetMediaSubdirectory(int mediaType)
    {
        // MediaType enum values:
        // None = 0, Animation = 1, Video = 2, Audio = 3, Voice = 4, Sticker = 5, VideoNote = 6, Document = 7, Photo = 8
        return mediaType switch
        {
            1 => "video",    // Animation
            2 => "video",    // Video
            6 => "video",    // VideoNote
            3 => "audio",    // Audio
            4 => "audio",    // Voice
            5 => "sticker",  // Sticker
            7 => "document", // Document
            8 => "photo",    // Photo
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
