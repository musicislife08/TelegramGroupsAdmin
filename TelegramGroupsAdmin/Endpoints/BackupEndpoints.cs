using System.Security.Claims;
using System.Text.RegularExpressions;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.Endpoints;

public static partial class BackupEndpoints
{
    [GeneratedRegex(@"^backup_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.tar\.gz$", RegexOptions.None)]
    private static partial Regex BackupFilenameRegex();

    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/backup/download/{filename}", async (
            string filename,
            IBackgroundJobConfigService jobConfigService,
            IAuditService auditService,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            // Validate filename format
            if (!BackupFilenameRegex().IsMatch(filename))
            {
                return Results.BadRequest("Invalid backup filename format.");
            }

            // Resolve backup directory from DB config, falling back to default
            var jobConfig = await jobConfigService.GetJobConfigAsync(BackgroundJobNames.ScheduledBackup, cancellationToken);
            var backupDirectory = jobConfig?.ScheduledBackup?.BackupDirectory
                ?? BackupRetentionConstants.DefaultBackupDirectory;

            // Path traversal protection
            var resolvedDirectory = Path.GetFullPath(backupDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(resolvedDirectory, filename));

            if (!fullPath.StartsWith(resolvedDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Results.BadRequest("Invalid file path.");
            }

            if (!File.Exists(fullPath))
            {
                return Results.NotFound("Backup file not found.");
            }

            // Audit log the download (security-sensitive action)
            var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
            var email = httpContext.User.FindFirstValue(ClaimTypes.Email);
            var actor = Actor.FromWebUser(userId, email);
            var fileSize = new FileInfo(fullPath).Length;

            await auditService.LogEventAsync(
                AuditEventType.DataExported,
                actor: actor,
                target: null,
                value: $"Downloaded backup: {filename} ({FormatBytes(fileSize)})",
                cancellationToken: cancellationToken);

            return Results.File(fullPath, "application/gzip", filename, enableRangeProcessing: true);
        }).RequireAuthorization(AuthenticationConstants.PolicyGlobalAdminOrOwner);

        return endpoints;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F2} {sizes[order]}";
    }
}
