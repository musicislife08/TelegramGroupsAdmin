namespace TelegramGroupsAdmin.Configuration.Models;

/// <summary>
/// General file scanning settings
/// </summary>
public class GeneralConfig
{
    /// <summary>
    /// Enable result caching (24hr TTL)
    /// </summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>
    /// Cache TTL in hours
    /// </summary>
    public int CacheTtlHours { get; set; } = 24;

    /// <summary>
    /// File types to scan (by extension)
    /// Empty list = scan all files
    /// </summary>
    public List<string> ScanFileTypes { get; set; } =
    [
        ".exe", ".dll", ".zip", ".rar", ".7z", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".apk", ".dmg",
        ".pkg", ".bat", ".ps1", ".sh"
    ];

    /// <summary>
    /// Maximum file size to scan in bytes
    /// Default: 4.5GB (Telegram Premium supports up to 4GB files)
    /// Set lower for performance if large file scans are too slow
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 4831838208;  // 4.5GB in bytes

    // AlwaysRunForAllUsers moved to ContentDetectionConfig.FileScanning.AlwaysRun
}
