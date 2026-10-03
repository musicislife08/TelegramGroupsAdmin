namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// The checks the passphrase rotation dialog runs before queuing a rotation: find backups an earlier
/// rotation damaged, repair them with the passphrase the user remembers, or delete them.
/// </summary>
public interface IBackupRotationService
{
    /// <summary>
    /// Classifies every backup in <paramref name="backupDirectory"/>. A missing directory scans as empty.
    /// </summary>
    Task<BackupRotationScan> ScanAsync(string backupDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries <paramref name="originalPassphrase"/> on every damaged backup in the directory. Each one it
    /// opens is rewritten as a normal backup on the stored passphrase.
    /// </summary>
    Task<WrappedRepairResult> RepairWrappedAsync(string backupDirectory, string originalPassphrase, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the named files from the directory, but only names that resolve inside it and files that
    /// are still damaged or unreadable. Healthy backups are never deleted.
    /// </summary>
    /// <returns>The number of files deleted.</returns>
    Task<int> DeleteDamagedAsync(string backupDirectory, IReadOnlyCollection<string> fileNames, string userId, CancellationToken cancellationToken = default);
}
