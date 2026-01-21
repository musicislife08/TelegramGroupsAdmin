namespace TelegramGroupsAdmin.Telegram.Models;

/// <summary>
/// Domain model for ban celebration GIF records
/// </summary>
public class BanCelebrationGif
{
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

    public DateTimeOffset CreatedAt { get; set; }
}
