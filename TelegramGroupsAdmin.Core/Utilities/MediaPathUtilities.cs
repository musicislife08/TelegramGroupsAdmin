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
        // Animation = 0, Video = 1, Audio = 2, Voice = 3, Sticker = 4, VideoNote = 5, Document = 6
        return mediaType switch
        {
            0 => "video",    // Animation
            1 => "video",    // Video
            5 => "video",    // VideoNote
            2 => "audio",    // Audio
            3 => "audio",    // Voice
            4 => "sticker",  // Sticker
            6 => "document", // Document
            _ => "other"
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
