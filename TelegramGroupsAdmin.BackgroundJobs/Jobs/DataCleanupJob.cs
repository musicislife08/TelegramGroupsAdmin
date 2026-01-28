using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Quartz job to clean up expired data: messages + disk files, reports, callback contexts, and notifications.
/// Replaces the legacy CleanupBackgroundService (BackgroundService with while-loop pattern).
/// </summary>
[DisallowConcurrentExecution]
public class DataCleanupJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageHistoryOptions _options;
    private readonly ILogger<DataCleanupJob> _logger;

    public DataCleanupJob(
        IServiceScopeFactory scopeFactory,
        IOptions<MessageHistoryOptions> options,
        ILogger<DataCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        const string jobName = BackgroundJobNames.DataCleanup;
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            await ExecuteCleanupAsync(context.CancellationToken);
            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data cleanup");
            throw; // Re-throw for Quartz retry logic
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }

    private async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // Get retention settings from job config
        var configService = sp.GetRequiredService<IBackgroundJobConfigService>();
        var jobConfig = await configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        var settings = jobConfig?.DataCleanup ?? new DataCleanupSettings();

        var messageRetention = TimeSpanUtilities.ParseDurationOrDefault(settings.MessageRetention, DataCleanupSettings.DefaultMessageRetention);
        var reportRetention = TimeSpanUtilities.ParseDurationOrDefault(settings.ReportRetention, DataCleanupSettings.DefaultReportRetention);
        var contextRetention = TimeSpanUtilities.ParseDurationOrDefault(settings.CallbackContextRetention, DataCleanupSettings.DefaultShortRetention);
        var notificationRetention = TimeSpanUtilities.ParseDurationOrDefault(settings.WebNotificationRetention, DataCleanupSettings.DefaultShortRetention);

        _logger.LogInformation("Data cleanup job started");

        // 1. Clean up expired messages + disk files
        await CleanupMessagesAsync(sp, messageRetention, cancellationToken);

        // 2. Clean up old resolved reports
        await CleanupReportsAsync(sp, reportRetention, cancellationToken);

        // 3. Clean up expired DM callback contexts
        await CleanupCallbackContextsAsync(sp, contextRetention, cancellationToken);

        // 4. Clean up old read web notifications
        await CleanupNotificationsAsync(sp, notificationRetention, cancellationToken);

        _logger.LogInformation("Data cleanup job completed");
    }

    private async Task CleanupMessagesAsync(IServiceProvider sp, TimeSpan retention, CancellationToken ct)
    {
        var repository = sp.GetRequiredService<IMessageHistoryRepository>();
        var result = await repository.CleanupExpiredAsync(retention, ct);

        // Delete image files from disk (photo thumbnails)
        var imageDeletedCount = 0;
        var basePath = _options.ImageStoragePath;
        foreach (var relativePath in result.ImagePaths)
        {
            try
            {
                var fullPath = Path.Combine(basePath, relativePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    imageDeletedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete image file: {Path}", relativePath);
            }
        }

        // Delete media files from disk (videos, animations, audio, voice, stickers, video notes)
        var mediaDeletedCount = 0;
        foreach (var relativePath in result.MediaPaths)
        {
            try
            {
                var fullPath = Path.Combine(basePath, relativePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    mediaDeletedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete media file: {Path}", relativePath);
            }
        }

        // Stats come from the repository result (repository is domain expert for message data)
        _logger.LogInformation(
            "Message cleanup: {Deleted} expired messages removed, {ImagesDeleted} images deleted, {MediaDeleted} media files deleted (retention: {Retention}). Stats: {Messages} messages, {Users} users, {Photos} photos, oldest: {Oldest}",
            result.DeletedCount,
            imageDeletedCount,
            mediaDeletedCount,
            TimeSpanUtilities.FormatDuration(retention),
            result.RemainingMessages,
            result.RemainingUniqueUsers,
            result.RemainingPhotos,
            result.OldestTimestamp.HasValue
                ? result.OldestTimestamp.Value.ToString("g")
                : "none");
    }

    private async Task CleanupReportsAsync(IServiceProvider sp, TimeSpan retention, CancellationToken ct)
    {
        var reportsRepo = sp.GetRequiredService<IReportsRepository>();
        var reportsCutoff = DateTimeOffset.UtcNow - retention;
        var reportsDeleted = await reportsRepo.DeleteOldReportsAsync(reportsCutoff, type: null, ct);

        if (reportsDeleted > 0)
        {
            _logger.LogInformation(
                "Report cleanup: {Count} old resolved reports deleted (retention: {Retention})",
                reportsDeleted,
                TimeSpanUtilities.FormatDuration(retention));
        }
    }

    private async Task CleanupCallbackContextsAsync(IServiceProvider sp, TimeSpan retention, CancellationToken ct)
    {
        var callbackContextRepo = sp.GetRequiredService<IReportCallbackContextRepository>();
        var contextsDeleted = await callbackContextRepo.DeleteExpiredAsync(retention, ct);

        if (contextsDeleted > 0)
        {
            _logger.LogInformation(
                "Callback context cleanup: {Count} expired contexts deleted (retention: {Retention})",
                contextsDeleted,
                TimeSpanUtilities.FormatDuration(retention));
        }
    }

    private async Task CleanupNotificationsAsync(IServiceProvider sp, TimeSpan retention, CancellationToken ct)
    {
        // Use repository directly - it's the domain expert for notification data
        var notificationRepo = sp.GetRequiredService<IWebNotificationRepository>();
        var notificationsDeleted = await notificationRepo.DeleteOldReadNotificationsAsync(retention, ct);

        if (notificationsDeleted > 0)
        {
            _logger.LogInformation(
                "Notification cleanup: {Count} old read notifications deleted (retention: {Retention})",
                notificationsDeleted,
                TimeSpanUtilities.FormatDuration(retention));
        }
    }
}
