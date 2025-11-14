using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Core.Security;
using TelegramGroupsAdmin.Data.Services;
using TelegramGroupsAdmin.Telegram.Abstractions.JobPayloads;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using DataModels = TelegramGroupsAdmin.Data.Models;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <summary>
/// Manages backup encryption passphrases and configuration lifecycle
/// </summary>
public class PassphraseManagementService : IPassphraseManagementService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDataProtectionService _totpProtection;
    private readonly IBackupConfigurationService _configService;
    private readonly IJobScheduler _jobScheduler;
    private readonly ILogger<PassphraseManagementService> _logger;

    public PassphraseManagementService(
        NpgsqlDataSource dataSource,
        IDataProtectionService totpProtection,
        IBackupConfigurationService configService,
        IJobScheduler jobScheduler,
        ILogger<PassphraseManagementService> logger)
    {
        _dataSource = dataSource;
        _totpProtection = totpProtection;
        _configService = configService;
        _jobScheduler = jobScheduler;
        _logger = logger;
    }

    /// <summary>
    /// Sets up initial backup encryption configuration
    /// </summary>
    public async Task SaveEncryptionConfigAsync(string passphrase)
    {
        var encryptedPassphrase = EncryptPassphrase(passphrase);
        var config = CreateNewEncryptionConfig();
        await SaveEncryptionConfigToDatabaseAsync(config, encryptedPassphrase);
    }

    /// <summary>
    /// Updates existing backup encryption configuration with new passphrase
    /// </summary>
    public async Task UpdateEncryptionConfigAsync(string passphrase)
    {
        var encryptedPassphrase = EncryptPassphrase(passphrase);
        var config = await UpdateExistingEncryptionConfigAsync();
        await SaveEncryptionConfigToDatabaseAsync(config, encryptedPassphrase);
    }

    /// <summary>
    /// Gets the current decrypted passphrase from database
    /// </summary>
    public async Task<string> GetDecryptedPassphraseAsync()
    {
        await using var context = await _dataSource.OpenConnectionAsync();

        // Read encrypted passphrase from dedicated column
        var encryptedPassphrase = await context.QuerySingleOrDefaultAsync<string>(
            "SELECT passphrase_encrypted FROM configs WHERE chat_id = 0");

        if (string.IsNullOrEmpty(encryptedPassphrase))
        {
            throw new InvalidOperationException("No passphrase found in encryption config");
        }

        return _totpProtection.Unprotect(encryptedPassphrase);
    }

    /// <summary>
    /// Rotates the backup passphrase and schedules re-encryption of existing backups
    /// </summary>
    public async Task<string> RotatePassphraseAsync(string backupDirectory, string userId)
    {
        _logger.LogInformation("Initiating passphrase rotation for user {UserId}", userId);

        // Generate new passphrase (6 words = 77.5 bits entropy)
        var newPassphrase = PassphraseGenerator.Generate();

        _logger.LogInformation("Generated new passphrase, queuing re-encryption job");

        // Queue background job to re-encrypt all backups
        var payload = new RotateBackupPassphrasePayload(newPassphrase, backupDirectory, userId);

        var jobId = await _jobScheduler.ScheduleJobAsync(
            "rotate_backup_passphrase",
            payload,
            delaySeconds: 0); // Execute immediately

        _logger.LogInformation("Passphrase rotation job queued successfully (JobId: {JobId})", jobId);

        // Return the new passphrase so user can save it
        return newPassphrase;
    }

    // Private helper methods

    private string EncryptPassphrase(string passphrase)
    {
        return _totpProtection.Protect(passphrase);
    }

    private BackupEncryptionConfig CreateNewEncryptionConfig()
    {
        return new BackupEncryptionConfig
        {
            Enabled = true,
            Algorithm = "AES-256-GCM",
            Iterations = 100000,
            CreatedAt = DateTimeOffset.UtcNow,
            LastRotatedAt = null  // First setup
        };
    }

    private async Task<BackupEncryptionConfig> UpdateExistingEncryptionConfigAsync()
    {
        var existing = await _configService.GetEncryptionConfigAsync();
        if (existing == null)
        {
            throw new InvalidOperationException("Cannot update encryption config - no existing configuration found");
        }

        existing.LastRotatedAt = DateTimeOffset.UtcNow;

        return existing;
    }

    private async Task SaveEncryptionConfigToDatabaseAsync(BackupEncryptionConfig config, string encryptedPassphrase)
    {
        await using var context = await _dataSource.OpenConnectionAsync();

        // Load or create global config record
        var configRecord = await context.QueryFirstOrDefaultAsync<DataModels.ConfigRecordDto>(
            "SELECT * FROM configs WHERE chat_id = 0");

        if (configRecord == null)
        {
            configRecord = new DataModels.ConfigRecordDto
            {
                ChatId = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        // Save config metadata to JSONB (without passphrase)
        configRecord.BackupEncryptionConfig = JsonSerializer.Serialize(config);
        // Save encrypted passphrase to dedicated TEXT column
        configRecord.PassphraseEncrypted = encryptedPassphrase;
        configRecord.UpdatedAt = DateTimeOffset.UtcNow;

        if (configRecord.Id == 0)
        {
            // Insert
            await context.ExecuteAsync(
                @"INSERT INTO configs (chat_id, backup_encryption_config, passphrase_encrypted, created_at, updated_at)
                  VALUES (@ChatId, @BackupEncryptionConfig::jsonb, @PassphraseEncrypted, @CreatedAt, @UpdatedAt)",
                configRecord);
        }
        else
        {
            // Update
            await context.ExecuteAsync(
                @"UPDATE configs
                  SET backup_encryption_config = @BackupEncryptionConfig::jsonb,
                      passphrase_encrypted = @PassphraseEncrypted,
                      updated_at = @UpdatedAt
                  WHERE id = @Id",
                configRecord);
        }

        _logger.LogInformation("Saved backup encryption config and passphrase to database");
    }
}
