using TelegramGroupsAdmin.Configuration.Models;

namespace TelegramGroupsAdmin.Services.Backup;

/// <summary>
/// Reads backup configuration settings from the database
/// Separated from backup operations and passphrase management for single responsibility
/// </summary>
public interface IBackupConfigurationService
{
    /// <summary>
    /// Retrieves backup encryption configuration from database
    /// </summary>
    Task<BackupEncryptionConfig?> GetEncryptionConfigAsync();
}
