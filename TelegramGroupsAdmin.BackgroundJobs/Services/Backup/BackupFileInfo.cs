namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Information about a backup file for retention analysis
/// </summary>
public class BackupFileInfo
{
    public required string FilePath { get; init; }
    public string FileName => Path.GetFileName(FilePath); // Computed property
    public required DateTimeOffset CreatedAt { get; init; }
    public required long FileSizeBytes { get; init; }
    public bool IsEncrypted { get; init; } // For backup browser
    public BackupTier? HighestTier { get; set; } // Calculated by retention service

    /// <summary>
    /// Parse timestamp from backup filename (backup_YYYY-MM-DD_HH-mm-ss.tar.gz).
    /// File creation time is unreliable on Linux/Docker, so we use the embedded timestamp.
    /// </summary>
    public static DateTimeOffset ParseTimestampFromFilename(string filepath)
    {
        var filename = Path.GetFileName(filepath);

        // Format: backup_YYYY-MM-DD_HH-mm-ss.tar.gz - timestamp at positions [7..26]
        if (filename.Length >= 26 &&
            DateTimeOffset.TryParseExact(
                filename[7..26],
                "yyyy-MM-dd_HH-mm-ss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }

        // Fallback to file modification time (more reliable than creation time on Linux)
        return new DateTimeOffset(File.GetLastWriteTimeUtc(filepath), TimeSpan.Zero);
    }
}
