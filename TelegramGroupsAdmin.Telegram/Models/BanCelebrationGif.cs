namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for ban celebration GIF records
/// </summary>
public class BanCelebrationGif
{
    /// <summary>
    /// Largest GIF or video the library accepts, whether uploaded or downloaded from a URL
    /// (50 MB, the Telegram Bot API's send ceiling). The upload dialog and the URL fetch cap
    /// both read this.
    /// </summary>
    public const long MaxFileBytes = 50 * 1024 * 1024;

    public int Id { get; set; }

    /// <summary>
    /// Relative file path within /data/media/ (e.g., "ban-gifs/1.gif")
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Cached Telegram file_id for instant re-sending
    /// </summary>
    public string? FileId { get; set; }

    /// <summary>
    /// Friendly display name for the GIF
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Relative path to thumbnail (first frame) for preview display
    /// </summary>
    public string? ThumbnailPath { get; set; }

    /// <summary>
    /// Perceptual hash (aHash) for duplicate detection - 64-bit hash stored as 8 bytes
    /// </summary>
    public byte[]? PhotoHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
