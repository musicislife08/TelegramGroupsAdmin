namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Result of creating a backup with retention cleanup
/// </summary>
public record BackupResult(
    string Filename,
    string FilePath,
    long SizeBytes,
    int DeletedCount);
