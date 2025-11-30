using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.BackgroundJobs;
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

                // Create a scope to resolve the repository and config service
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();
                var configService = scope.ServiceProvider.GetRequiredService<IBackgroundJobConfigService>();

                // Check if job is enabled
                if (!await configService.IsJobEnabledAsync(BackgroundJobNames.MessageCleanup))
                {
                    _logger.LogDebug("Message cleanup job is disabled, skipping");
                    continue;
                }

                var (deleted, imagePaths, mediaPaths) = await repository.CleanupExpiredAsync();

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
                    "Cleanup complete: {Deleted} expired messages removed, {ImagesDeleted} images deleted, {MediaDeleted} media files deleted. Stats: {Messages} messages, {Users} users, {Photos} photos, oldest: {Oldest}",
                    deleted,
                    imageDeletedCount,
                    mediaDeletedCount,
                    stats.TotalMessages,
                    stats.UniqueUsers,
                    stats.PhotoCount,
                    stats.OldestTimestamp.HasValue
                        ? stats.OldestTimestamp.Value.ToString("g")
                        : "none");

                // Clean up old read web notifications (7 day retention)
                var webPushService = scope.ServiceProvider.GetRequiredService<IWebPushNotificationService>();
                var notificationsDeleted = await webPushService.DeleteOldReadNotificationsAsync(
                    TimeSpan.FromDays(7), stoppingToken);

                if (notificationsDeleted > 0)
                {
                    _logger.LogInformation(
                        "Notification cleanup: {Count} old read notifications deleted",
                        notificationsDeleted);
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
}
