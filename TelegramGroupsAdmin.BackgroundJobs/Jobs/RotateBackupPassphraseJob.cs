using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Helpers;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job for rotating the backup encryption passphrase. Re-encrypts the database entry inside every
/// backup archive with the new passphrase, then stores the new passphrase only if no file failed.
/// Backups damaged by older versions of this job (whole archive encrypted) are skipped; the rotation
/// dialog offers to repair or delete them before the job is queued.
/// </summary>
[DisallowConcurrentExecution]
public class RotateBackupPassphraseJob(
    IBackupArchiveRotator rotator,
    IPassphraseManagementService passphraseService,
    IDataProtectionService dataProtection,
    IServiceScopeFactory scopeFactory,
    ILogger<RotateBackupPassphraseJob> logger,
    JobMetrics jobMetrics) : IJob
{
    /// <summary>
    /// Execute passphrase rotation (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        var payload = await JobPayloadHelper.TryGetPayloadAsync<RotateBackupPassphrasePayload>(context, logger);
        if (payload == null) return;

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
            if (string.IsNullOrEmpty(payload.ProtectedNewPassphrase))
            {
                logger.LogError("Passphrase rotation job was queued by an older version and carries no protected passphrase. " +
                                "Nothing was changed; start the rotation again from Settings → Backup & Restore");
                return;
            }

            var userId = payload.UserId; // Web user GUID string
            var backupDirectory = payload.BackupDirectory;
            var newPassphrase = dataProtection.Unprotect(payload.ProtectedNewPassphrase);

            logger.LogInformation("Starting passphrase rotation for user {UserId} in directory {Directory}", userId, backupDirectory);

            try
            {
                var oldPassphrase = await passphraseService.GetDecryptedPassphraseAsync();

                if (!Directory.Exists(backupDirectory))
                {
                    logger.LogWarning("Backup directory {Directory} does not exist, creating it", backupDirectory);
                    Directory.CreateDirectory(backupDirectory);
                }

                var backupFiles = Directory.GetFiles(backupDirectory, "*.tar.gz");
                logger.LogInformation("Found {Count} backup files to re-encrypt", backupFiles.Length);

                int reencryptedCount = 0;
                int currentCount = 0;
                int skippedCount = 0;
                int failedCount = 0;

                foreach (var backupFile in backupFiles)
                {
                    var fileName = Path.GetFileName(backupFile);
                    try
                    {
                        switch (await rotator.InspectAsync(backupFile, cancellationToken))
                        {
                            case BackupFileState.Encrypted or BackupFileState.Plain:
                                var outcome = await rotator.ReencryptAsync(backupFile, oldPassphrase, newPassphrase, cancellationToken);
                                if (outcome == ReencryptOutcome.AlreadyCurrent)
                                    currentCount++;
                                else
                                    reencryptedCount++;
                                break;

                            case BackupFileState.Wrapped:
                                skippedCount++;
                                logger.LogWarning("Skipping backup {FileName}: it was damaged by an earlier rotation. " +
                                                  "Repair or delete it from the rotation dialog", fileName);
                                break;

                            default:
                                failedCount++;
                                logger.LogError("❌ Backup {FileName} cannot be read as a backup archive", fileName);
                                break;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failedCount++;
                        logger.LogError(ex, "❌ Failed to re-encrypt backup: {FileName}", fileName);
                    }
                }

                // Update config with new passphrase ONLY if no backup failed.
                // Fail-fast: preserve old passphrase in DB; files already rotated count as current on retry.
                if (failedCount > 0)
                {
                    logger.LogError("❌ Passphrase rotation failed: {Success} rotated, {Failed} failed - keeping old passphrase in database",
                        reencryptedCount + currentCount, failedCount);
                    throw new InvalidOperationException($"Failed to re-encrypt {failedCount} backup file(s). Old passphrase preserved in database. Resolve underlying issues and retry the job.");
                }

                await passphraseService.UpdateEncryptionConfigAsync(newPassphrase);
                logger.LogInformation("✅ Passphrase rotation complete: {Reencrypted} re-encrypted, {Current} already current, {Skipped} damaged skipped",
                    reencryptedCount, currentCount, skippedCount);

                // Audit log the passphrase rotation (uses IServiceScopeFactory to avoid circular dependency)
                await using var scope = scopeFactory.CreateAsyncScope();
                var auditService = scope.ServiceProvider.GetService<TelegramGroupsAdmin.Core.Services.IAuditService>();
                if (auditService != null)
                {
                    await auditService.LogEventAsync(
                        AuditEventType.BackupPassphraseRotated,
                        actor: Actor.FromWebUser(userId),
                        target: null,
                        value: $"Re-encrypted {reencryptedCount}, already current {currentCount}, skipped {skippedCount} damaged backup(s) in {backupDirectory}",
                        cancellationToken: cancellationToken);
                }
                else
                {
                    logger.LogWarning("IAuditService not available, skipping audit log");
                }

                success = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "❌ Passphrase rotation failed");
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            jobMetrics.RecordJobExecution(jobName, success, elapsedMs);
        }
    }
}
