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
}
