using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Ui.Models;
using TelegramGroupsAdmin.Ui.Server.Services;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints.Actions;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /api/backup/check-encrypted - Check if backup file is encrypted
        // Only allowed during first-run (no users exist) for restore functionality
        endpoints.MapPost("/api/backup/check-encrypted", async (
            [FromBody] BackupFileRequest request,
            [FromServices] IBackupService backupService,
            [FromServices] IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            // Security: Only allow anonymous access during first-run
            if (!await authService.IsFirstRunAsync(cancellationToken))
            {
                return Results.Unauthorized();
            }

            try
            {
                var backupBytes = Convert.FromBase64String(request.BackupBase64);
                var isEncrypted = await backupService.IsEncryptedAsync(backupBytes);
                return Results.Json(new { success = true, isEncrypted });
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, error = ex.Message });
            }
        }).AllowAnonymous(); // Anonymous but gated by IsFirstRunAsync check

        // POST /api/backup/metadata - Get backup metadata
        // Only allowed during first-run (no users exist) for restore functionality
        endpoints.MapPost("/api/backup/metadata", async (
            [FromBody] BackupMetadataRequest request,
            [FromServices] IBackupService backupService,
            [FromServices] IAuthService authService,
            CancellationToken cancellationToken) =>
        {
            // Security: Only allow anonymous access during first-run
            if (!await authService.IsFirstRunAsync(cancellationToken))
            {
                return Results.Unauthorized();
            }

            try
            {
                var backupBytes = Convert.FromBase64String(request.BackupBase64);

                BackupMetadata metadata;
                if (!string.IsNullOrWhiteSpace(request.Passphrase))
                {
                    metadata = await backupService.GetMetadataAsync(backupBytes, request.Passphrase);
                }
                else
                {
                    metadata = await backupService.GetMetadataAsync(backupBytes);
                }

                return Results.Json(new
                {
                    success = true,
                    metadata = new
                    {
                        version = metadata.Version,
                        createdAt = metadata.CreatedAt,
                        appVersion = metadata.AppVersion,
                        tableCount = metadata.TableCount,
                        tables = metadata.Tables
                    }
                });
            }
            catch (Exception ex)
            {
                return Results.Json(new { success = false, error = ex.Message });
            }
        }).AllowAnonymous(); // Anonymous but gated by IsFirstRunAsync check

        // POST /api/backup/restore - Restore from backup
        // Only allowed during first-run (no users exist) for restore functionality
        endpoints.MapPost("/api/backup/restore", async (
            [FromBody] BackupRestoreRequest request,
            [FromServices] IBackupService backupService,
            [FromServices] IAuthService authService,
            [FromServices] ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            // Security: Only allow anonymous access during first-run
            if (!await authService.IsFirstRunAsync(cancellationToken))
            {
                return Results.Unauthorized();
            }

            try
            {
                var backupBytes = Convert.FromBase64String(request.BackupBase64);

                logger.LogInformation("Starting backup restore...");

                if (!string.IsNullOrWhiteSpace(request.Passphrase))
                {
                    await backupService.RestoreAsync(backupBytes, request.Passphrase);
                }
                else
                {
                    await backupService.RestoreAsync(backupBytes);
                }

                logger.LogInformation("Backup restore completed successfully");

                return Results.Json(new { success = true });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backup restore failed");
                return Results.Json(new { success = false, error = ex.Message });
            }
        }).AllowAnonymous(); // Anonymous but gated by IsFirstRunAsync check

        return endpoints;
    }
}
