namespace TelegramGroupsAdmin.Data.Models.Configs;

/// <summary>
/// Data layer representation of GeneralConfig (file scanning) for EF Core JSON column mapping.
/// </summary>
public class GeneralConfigData
{
    /// <summary>
    /// Enable result caching
    /// </summary>
    public bool CacheEnabled { get; set; } = true;

    /// <summary>
    /// Cache TTL in hours
    /// </summary>
    public int CacheTtlHours { get; set; } = 24;

    /// <summary>
    /// File types to scan (by extension)
    /// </summary>
    public List<string> ScanFileTypes { get; set; } =
    [
        ".exe", ".dll", ".zip", ".rar", ".7z", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".apk", ".dmg",
        ".pkg", ".bat", ".ps1", ".sh"
    ];

    /// <summary>
    /// Maximum file size to scan in bytes
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 4831838208; // 4.5GB

    // AlwaysRunForAllUsers moved to ContentDetectionConfigData.FileScanning.AlwaysRun
}
