namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request structure for content detection, based on tg-spam's Request model
/// </summary>
public record ContentCheckRequest
{
    /// <summary>
    /// Message text to check for spam
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Telegram user ID who sent the message
    /// </summary>
    public required long UserId { get; init; }

    /// <summary>
    /// Telegram username (may be null)
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// Message metadata from Telegram
    /// </summary>
    public ContentCheckMetadata Metadata { get; init; } = new();

    /// <summary>
    /// Chat ID for per-chat configuration
    /// </summary>
    public required long ChatId { get; init; }

    /// <summary>
    /// If true, don't update approved users database
    /// </summary>
    public bool CheckOnly { get; init; } = false;

    /// <summary>
    /// Whether other spam checks have flagged this message as spam
    /// </summary>
    public bool HasSpamFlags { get; init; } = false;

    /// <summary>
    /// Image data for image spam detection (optional)
    /// </summary>
    public Stream? ImageData { get; init; }

    /// <summary>
    /// Image file name or type (optional)
    /// </summary>
    public string? ImageFileName { get; init; }

    /// <summary>
    /// List of URLs extracted from the message (for URL-based checks)
    /// </summary>
    public List<string> Urls { get; init; } = [];

    /// <summary>
    /// Telegram photo file ID (for image spam detection)
    /// </summary>
    public string? PhotoFileId { get; init; }

    /// <summary>
    /// Photo URL (for image spam detection)
    /// </summary>
    public string? PhotoUrl { get; init; }
}
