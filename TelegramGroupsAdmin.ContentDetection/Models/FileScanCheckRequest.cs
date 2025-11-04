namespace TelegramGroupsAdmin.ContentDetection.Models;

/// <summary>
/// Request structure for file scanning checks
/// Used when a Telegram message contains a document/file attachment
/// Phase 6: Refactored to use file path instead of in-memory bytes for large file support
/// </summary>
public class FileScanCheckRequest : ContentCheckRequestBase
{
    /// <summary>
    /// Path to the temporary file to scan (downloaded from Telegram)
    /// Scanners open their own streams from this path to enable parallel scanning
    /// File will be deleted by caller after all scans complete
    /// </summary>
    public required string FilePath { get; init; }

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
