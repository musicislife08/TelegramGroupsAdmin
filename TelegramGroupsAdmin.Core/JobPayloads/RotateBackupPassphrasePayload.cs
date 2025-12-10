namespace TelegramGroupsAdmin.Core.JobPayloads;

/// <summary>
/// Payload for rotating backup encryption passphrase.
/// Re-encrypts all existing backups with a new passphrase using atomic file operations.
/// </summary>
/// <param name="NewPassphrase">The newly generated passphrase to use for re-encryption</param>
/// <param name="BackupDirectory">Directory containing backups to re-encrypt (default: /data/backups)</param>
/// <param name="UserId">User who initiated the rotation (for audit logging) - web user GUID</param>
public record RotateBackupPassphrasePayload(
    string NewPassphrase,
    string BackupDirectory,
    string UserId
);
