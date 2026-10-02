using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

/// <inheritdoc />
public class BackupRotationService(
    IBackupArchiveRotator rotator,
    IPassphraseManagementService passphraseService,
    BackupFileLock fileLock,
    IServiceScopeFactory scopeFactory,
    ILogger<BackupRotationService> logger) : IBackupRotationService
{
    public async Task<BackupRotationScan> ScanAsync(string backupDirectory, CancellationToken cancellationToken = default)
    {
        var rotatable = 0;
        List<string> wrapped = [];
        List<string> unreadable = [];

        foreach (var path in BackupFiles(backupDirectory))
        {
            switch (await rotator.InspectAsync(path, cancellationToken))
            {
                case BackupFileState.Encrypted or BackupFileState.Plain:
                    rotatable++;
                    break;
                case BackupFileState.Wrapped:
                    wrapped.Add(Path.GetFileName(path));
                    break;
                default:
                    unreadable.Add(Path.GetFileName(path));
                    break;
            }
        }

        return new BackupRotationScan(rotatable, wrapped, unreadable);
    }

    public async Task<WrappedRepairResult> RepairWrappedAsync(string backupDirectory, string originalPassphrase, string userId, CancellationToken cancellationToken = default)
    {
        using var heldLock = await fileLock.AcquireAsync(cancellationToken);
        var storedPassphrase = await passphraseService.GetDecryptedPassphraseAsync();
        var repaired = 0;
        List<string> stillWrapped = [];

        foreach (var path in BackupFiles(backupDirectory))
        {
            if (await rotator.InspectAsync(path, cancellationToken) != BackupFileState.Wrapped)
                continue;

            var fileName = Path.GetFileName(path);
            try
            {
                if (await rotator.TryRepairWrappedAsync(path, storedPassphrase, originalPassphrase, cancellationToken))
                {
                    repaired++;
                    continue;
                }
            }
            catch (CryptographicException ex)
            {
                logger.LogWarning(ex, "Damaged backup {FileName} does not open with the stored passphrase", fileName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Damaged backup {FileName} could not be repaired", fileName);
            }

            stillWrapped.Add(fileName);
        }

        if (repaired > 0)
            await AuditAsync(AuditEventType.BackupFilesRepaired, userId, $"Repaired {repaired} damaged backup(s) in {backupDirectory}", cancellationToken);

        return new WrappedRepairResult(repaired, stillWrapped);
    }

    public async Task<int> DeleteDamagedAsync(string backupDirectory, IReadOnlyCollection<string> fileNames, string userId, CancellationToken cancellationToken = default)
    {
        using var heldLock = await fileLock.AcquireAsync(cancellationToken);
        var root = Path.GetFullPath(backupDirectory) + Path.DirectorySeparatorChar;
        List<string> deleted = [];

        foreach (var name in fileNames)
        {
            if (Path.GetFileName(name) != name || !name.EndsWith(BackupFileExtension, StringComparison.Ordinal))
            {
                logger.LogWarning("Refusing to delete {Name}: not a backup file name", name);
                continue;
            }

            var path = Path.GetFullPath(Path.Combine(backupDirectory, name));
            if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
                continue;

            if (await rotator.InspectAsync(path, cancellationToken) is not (BackupFileState.Wrapped or BackupFileState.Unreadable))
            {
                logger.LogWarning("Refusing to delete {Name}: it is a readable backup", name);
                continue;
            }

            try
            {
                File.Delete(path);
                deleted.Add(name);
                logger.LogWarning("Deleted damaged backup {Name}", name);
            }
            catch (IOException ex)
            {
                // Keep going so the audit entry still records every file that was deleted
                logger.LogError(ex, "Failed to delete damaged backup {Name}", name);
            }
        }

        if (deleted.Count > 0)
            await AuditAsync(AuditEventType.BackupFilesDeleted, userId, $"Deleted damaged backup(s): {string.Join(", ", deleted)}", cancellationToken);

        return deleted.Count;
    }

    private const string BackupFileExtension = ".tar.gz";

    private static IEnumerable<string> BackupFiles(string backupDirectory) =>
        Directory.Exists(backupDirectory)
            ? Directory.GetFiles(backupDirectory, "*" + BackupFileExtension).Order(StringComparer.Ordinal)
            : [];

    // Audit through a scope: IAuditService is scoped, this service is resolved from Blazor circuits and jobs alike
    private async Task AuditAsync(AuditEventType eventType, string userId, string value, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var auditService = scope.ServiceProvider.GetService<IAuditService>();
        if (auditService == null)
        {
            logger.LogWarning("IAuditService not available, skipping audit log");
            return;
        }

        await auditService.LogEventAsync(eventType, Actor.FromWebUser(userId), target: null, value: value, cancellationToken: cancellationToken);
    }
}
