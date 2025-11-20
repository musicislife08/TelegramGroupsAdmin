using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Telegram.Abstractions.JobPayloads;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job for rotating backup encryption passphrase.
/// Re-encrypts all existing backups with new passphrase using atomic file operations.
/// </summary>
public class RotateBackupPassphraseJob : IJob
{
    private readonly IBackupEncryptionService _encryptionService;
    private readonly IBackupService _backupService;
    private readonly IPassphraseManagementService _passphraseService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RotateBackupPassphraseJob> _logger;

    public RotateBackupPassphraseJob(
        IBackupEncryptionService encryptionService,
        IBackupService backupService,
        IPassphraseManagementService passphraseService,
        IServiceScopeFactory scopeFactory,
        ILogger<RotateBackupPassphraseJob> logger)
    {
        _encryptionService = encryptionService;
        _backupService = backupService;
        _passphraseService = passphraseService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Execute passphrase rotation (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        var payloadJson = context.JobDetail.JobDataMap.GetString("payload")
            ?? throw new InvalidOperationException("payload not found in job data");

        var payload = JsonSerializer.Deserialize<RotateBackupPassphrasePayload>(payloadJson)
            ?? throw new InvalidOperationException("Failed to deserialize RotateBackupPassphrasePayload");

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute passphrase rotation (business logic)
    /// </summary>
    private async Task ExecuteAsync(RotateBackupPassphrasePayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "RotateBackupPassphrase";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            if (payload == null)
            {
                _logger.LogError("RotateBackupPassphraseJob received null payload");
                return;
            }
            var userId = payload.UserId; // Web user GUID string
            var newPassphrase = payload.NewPassphrase;
            var backupDirectory = payload.BackupDirectory;

            _logger.LogInformation("Starting passphrase rotation for user {UserId} in directory {Directory}", userId, backupDirectory);

            try
            {
                // Get decrypted old passphrase from database
                var oldPassphrase = await _passphraseService.GetDecryptedPassphraseAsync();

                // Find all backup files
                if (!Directory.Exists(backupDirectory))
                {
                    _logger.LogWarning("Backup directory {Directory} does not exist, creating it", backupDirectory);
                    Directory.CreateDirectory(backupDirectory);

                    // No backups to rotate, just update config
                    await UpdateConfigWithNewPassphrase(newPassphrase);
                    _logger.LogInformation("✅ Passphrase rotation complete: No backups found, config updated");
                    return;
                }

                var backupFiles = Directory.GetFiles(backupDirectory, "*.tar.gz")
                    .Where(f => !f.EndsWith(".new")) // Exclude temporary files
                    .ToList();

                _logger.LogInformation("Found {Count} backup files to re-encrypt", backupFiles.Count);

                if (backupFiles.Count == 0)
                {
                    // No backups to rotate, just update config
                    await UpdateConfigWithNewPassphrase(newPassphrase);
                    _logger.LogInformation("✅ Passphrase rotation complete: No backups found, config updated");
                    return;
                }

                int processedCount = 0;
                int failedCount = 0;

                foreach (var backupFile in backupFiles)
                {
                    var fileName = Path.GetFileName(backupFile);

                    try
                    {
                        _logger.LogInformation("Re-encrypting backup: {FileName}", fileName);

                        // Read original backup
                        var originalBytes = await File.ReadAllBytesAsync(backupFile);

                        // Decrypt with old passphrase (or use as-is if unencrypted)
                        byte[] decryptedBytes = _encryptionService.IsEncrypted(originalBytes)
                            ? _encryptionService.DecryptBackup(originalBytes, oldPassphrase)
                            : originalBytes;

                        // Encrypt with new passphrase
                        var reencryptedBytes = _encryptionService.EncryptBackup(decryptedBytes, newPassphrase);

                        // Atomic file operation: write to .new, validate, swap
                        var tempFile = $"{backupFile}.new";
                        await File.WriteAllBytesAsync(tempFile, reencryptedBytes);

                        // Validate new file
                        var validationBytes = await File.ReadAllBytesAsync(tempFile);
                        var testDecrypt = _encryptionService.DecryptBackup(validationBytes, newPassphrase);

                        if (testDecrypt.Length != decryptedBytes.Length)
                        {
                            throw new InvalidOperationException($"Validation failed: decrypted size mismatch ({testDecrypt.Length} != {decryptedBytes.Length})");
                        }

                        // Atomic swap
                        File.Move(tempFile, backupFile, overwrite: true);

                        processedCount++;
                        _logger.LogInformation("✅ Successfully re-encrypted: {FileName} ({Current}/{Total})", fileName, processedCount, backupFiles.Count);
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogError(ex, "❌ Failed to re-encrypt backup: {FileName}", fileName);

                        // Clean up temp file if it exists
                        var tempFile = $"{backupFile}.new";
                        if (File.Exists(tempFile))
                        {
                            try
                            {
                                File.Delete(tempFile);
                            }
                            catch (Exception deleteEx)
                            {
                                _logger.LogWarning(deleteEx, "Failed to delete temp file: {TempFile}", tempFile);
                            }
                        }
                    }
                }

                // Update config with new passphrase
                await UpdateConfigWithNewPassphrase(newPassphrase);

                // Log final status
                if (failedCount > 0)
                {
                    _logger.LogWarning("⚠️ Passphrase rotation completed with errors: {Success} successful, {Failed} failed", processedCount, failedCount);
                }
                else
                {
                    _logger.LogInformation("✅ Passphrase rotation complete: {Count} backups re-encrypted successfully", processedCount);
                }

                // Audit log the passphrase rotation (uses IServiceScopeFactory to avoid circular dependency)
                await using var scope = _scopeFactory.CreateAsyncScope();
                var auditService = scope.ServiceProvider.GetService<TelegramGroupsAdmin.Core.Services.IAuditService>();
                if (auditService != null)
                {
                    await auditService.LogEventAsync(
                        AuditEventType.BackupPassphraseRotated,
                        actor: Actor.FromWebUser(userId),
                        target: null,
                        value: $"Re-encrypted {processedCount} backup(s) in {backupDirectory}" + (failedCount > 0 ? $" ({failedCount} failed)" : ""),
                        ct: cancellationToken);

                    _logger.LogInformation("Audit log entry created for passphrase rotation");
                }
                else
                {
                    _logger.LogWarning("IAuditService not available, skipping audit log");
                }

                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Passphrase rotation failed");
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }

    private async Task UpdateConfigWithNewPassphrase(string newPassphrase)
    {
        // Use refactored service method
        await _passphraseService.UpdateEncryptionConfigAsync(newPassphrase);
        _logger.LogInformation("Updated encryption config with new passphrase");
    }
}
