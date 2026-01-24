using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Utilities;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.Services.BackgroundServices;

public class CleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MessageHistoryOptions _options;
    private readonly ILogger<CleanupBackgroundService> _logger;

    public CleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MessageHistoryOptions> options,
        ILogger<CleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cleanup service started (interval: {Interval} minutes)", _options.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_options.CleanupIntervalMinutes), stoppingToken);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var configService = scope.ServiceProvider.GetRequiredService<IBackgroundJobConfigService>();

                // Check if job is enabled
                if (!await configService.IsJobEnabledAsync(BackgroundJobNames.MessageCleanup))
                {
                    _logger.LogDebug("Message cleanup job is disabled, skipping");
                    continue;
                }

                // Get retention settings from job config (uses record defaults if not configured)
                var jobConfig = await configService.GetJobConfigAsync(BackgroundJobNames.MessageCleanup);
                var settings = jobConfig?.DataCleanup ?? new DataCleanupSettings();

                var messageRetention = ParseRetention(settings.MessageRetention, TimeSpan.FromDays(30));
                var reportRetention = ParseRetention(settings.ReportRetention, TimeSpan.FromDays(30));
                var contextRetention = ParseRetention(settings.CallbackContextRetention, TimeSpan.FromDays(7));
                var notificationRetention = ParseRetention(settings.WebNotificationRetention, TimeSpan.FromDays(7));

                // 1. Clean up expired messages
                var repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
                var (deleted, imagePaths, mediaPaths) = await repository.CleanupExpiredAsync(messageRetention, stoppingToken);

                // Delete image files from disk (photo thumbnails)
                var imageDeletedCount = 0;
                var basePath = _options.ImageStoragePath;
                foreach (var relativePath in imagePaths)
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
                foreach (var relativePath in mediaPaths)
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

                var statsService = scope.ServiceProvider.GetRequiredService<IMessageStatsService>();
                var stats = await statsService.GetStatsAsync();

                _logger.LogInformation(
                    "Message cleanup: {Deleted} expired messages removed, {ImagesDeleted} images deleted, {MediaDeleted} media files deleted (retention: {Retention}). Stats: {Messages} messages, {Users} users, {Photos} photos, oldest: {Oldest}",
                    deleted,
                    imageDeletedCount,
                    mediaDeletedCount,
                    TimeSpanUtilities.FormatDuration(messageRetention),
                    stats.TotalMessages,
                    stats.UniqueUsers,
                    stats.PhotoCount,
                    stats.OldestTimestamp.HasValue
                        ? stats.OldestTimestamp.Value.ToString("g")
                        : "none");

                // 2. Clean up old resolved reports
                var reportsRepo = scope.ServiceProvider.GetRequiredService<IReportsRepository>();
                var reportsCutoff = DateTimeOffset.UtcNow - reportRetention;
                var reportsDeleted = await reportsRepo.DeleteOldReportsAsync(reportsCutoff, type: null, stoppingToken);

                if (reportsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Report cleanup: {Count} old resolved reports deleted (retention: {Retention})",
                        reportsDeleted,
                        TimeSpanUtilities.FormatDuration(reportRetention));
                }

                // 3. Clean up expired DM callback contexts
                var callbackContextRepo = scope.ServiceProvider.GetRequiredService<IReportCallbackContextRepository>();
                var contextsDeleted = await callbackContextRepo.DeleteExpiredAsync(contextRetention, stoppingToken);

                if (contextsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Callback context cleanup: {Count} expired contexts deleted (retention: {Retention})",
                        contextsDeleted,
                        TimeSpanUtilities.FormatDuration(contextRetention));
                }

                // 4. Clean up old read web notifications
                var webPushService = scope.ServiceProvider.GetRequiredService<IWebPushNotificationService>();
                var notificationsDeleted = await webPushService.DeleteOldReadNotificationsAsync(
                    notificationRetention, stoppingToken);

                if (notificationsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Notification cleanup: {Count} old read notifications deleted (retention: {Retention})",
                        notificationsDeleted,
                        TimeSpanUtilities.FormatDuration(notificationRetention));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }
        }

        _logger.LogInformation("Cleanup service stopped");
    }

    private static TimeSpan ParseRetention(string durationString, TimeSpan defaultValue)
        => TimeSpanUtilities.TryParseDuration(durationString, out var duration) ? duration : defaultValue;
}
