namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for rotating backup encryption passphrase.
/// Re-encrypts all existing backups with a new passphrase using atomic file operations.
/// </summary>
/// <param name="ProtectedNewPassphrase">The new passphrase, protected with IDataProtectionService. The job store
/// persists payloads, so the passphrase is only unprotected inside the job.</param>
/// <param name="BackupDirectory">Directory containing backups to re-encrypt (default: /data/backups)</param>
/// <param name="UserId">User who initiated the rotation (for audit logging) - web user GUID</param>
public record RotateBackupPassphrasePayload(
    string ProtectedNewPassphrase,
    string BackupDirectory,
    string UserId
);
