namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request structure for file scanning checks
/// Used when a Telegram message contains a document/file attachment
/// </summary>
public class FileScanCheckRequest : ContentCheckRequestBase
{
    /// <summary>
    /// File bytes to scan (downloaded from Telegram)
    /// </summary>
    public required byte[] FileBytes { get; init; }

    /// <summary>
    /// Original file name from Telegram
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// SHA256 hash of the file (for caching)
    /// Calculated before scanning
    /// </summary>
    public required string FileHash { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// Telegram file ID for tracking
    /// </summary>
    public string? FileId { get; init; }
}
