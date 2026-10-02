namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Passphrase operations on a single backup archive. Only the database entry is ever re-encrypted;
/// metadata and media entries are copied unchanged. Every rewrite goes to a temp file, is verified by
/// decrypting the new entry, and then replaces the original in one move.
/// </summary>
public interface IBackupArchiveRotator
{
    /// <summary>
    /// Classifies a backup file without decrypting anything.
    /// </summary>
    Task<BackupFileState> InspectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encrypts the database entry of an <see cref="BackupFileState.Encrypted"/> or
    /// <see cref="BackupFileState.Plain"/> backup with <paramref name="newPassphrase"/>.
    /// A file whose entry already opens with the new passphrase is left untouched, so a rotation can be
    /// run again after a partial failure.
    /// </summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">Neither passphrase opens the entry.</exception>
    /// <exception cref="InvalidOperationException">The file is not an Encrypted or Plain backup.</exception>
    Task<ReencryptOutcome> ReencryptAsync(string path, string oldPassphrase, string newPassphrase, CancellationToken cancellationToken = default);

    /// <summary>
    /// Repairs a <see cref="BackupFileState.Wrapped"/> backup: removes the outer layer with
    /// <paramref name="storedPassphrase"/>, opens the database entry inside with
    /// <paramref name="originalPassphrase"/>, and re-encrypts it with the stored passphrase, leaving a
    /// normal backup.
    /// </summary>
    /// <returns>False, with the file untouched, when <paramref name="originalPassphrase"/> does not open the entry.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The stored passphrase does not open the outer layer.</exception>
    Task<bool> TryRepairWrappedAsync(string path, string storedPassphrase, string originalPassphrase, CancellationToken cancellationToken = default);
}
