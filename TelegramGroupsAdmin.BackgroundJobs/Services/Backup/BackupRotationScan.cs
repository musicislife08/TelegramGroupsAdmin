namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Result of checking a backup directory before a passphrase rotation.
/// </summary>
/// <param name="RotatableCount">Backups the rotation can re-encrypt.</param>
/// <param name="WrappedFiles">File names of backups damaged by an earlier rotation, sorted.</param>
/// <param name="UnreadableFiles">File names that are not readable backups and would fail the rotation, sorted.</param>
public record BackupRotationScan(int RotatableCount, IReadOnlyList<string> WrappedFiles, IReadOnlyList<string> UnreadableFiles);
