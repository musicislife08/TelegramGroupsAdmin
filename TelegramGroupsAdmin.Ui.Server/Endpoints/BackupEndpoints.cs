using Microsoft.AspNetCore.Mvc;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;

namespace TelegramGroupsAdmin.Ui.Server.Endpoints;

public static class BackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // POST /api/backup/check-encrypted - Check if backup file is encrypted
        endpoints.MapPost("/api/backup/check-encrypted", async (
            [FromBody] BackupFileRequest request,
            [FromServices] IBackupService backupService) =>
        {
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
        }).AllowAnonymous(); // Allow anonymous for first-run restore

        // POST /api/backup/metadata - Get backup metadata
        endpoints.MapPost("/api/backup/metadata", async (
            [FromBody] BackupMetadataRequest request,
            [FromServices] IBackupService backupService) =>
        {
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
        }).AllowAnonymous(); // Allow anonymous for first-run restore

        // POST /api/backup/restore - Restore from backup
        endpoints.MapPost("/api/backup/restore", async (
            [FromBody] BackupRestoreRequest request,
            [FromServices] IBackupService backupService,
            [FromServices] ILogger<Program> logger) =>
        {
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
        }).AllowAnonymous(); // Allow anonymous for first-run restore

        return endpoints;
    }

    // Request DTOs
    private record BackupFileRequest(string BackupBase64);
    private record BackupMetadataRequest(string BackupBase64, string? Passphrase = null);
    private record BackupRestoreRequest(string BackupBase64, string? Passphrase = null);
}
