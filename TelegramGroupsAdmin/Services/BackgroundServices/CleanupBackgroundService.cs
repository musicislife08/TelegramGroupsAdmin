using Microsoft.Extensions.Options;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Repositories;

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

                // Create a scope to resolve the repository
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<MessageHistoryRepository>();

                var (deleted, imagePaths) = await repository.CleanupExpiredAsync();

                // Delete image files from disk
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

                var stats = await repository.GetStatsAsync();

                _logger.LogInformation(
                    "Cleanup complete: {Deleted} expired messages removed, {ImagesDeleted} images deleted. Stats: {Messages} messages, {Users} users, {Photos} photos, oldest: {Oldest}",
                    deleted,
                    imageDeletedCount,
                    stats.TotalMessages,
                    stats.UniqueUsers,
                    stats.PhotoCount,
                    stats.OldestTimestamp.HasValue
                        ? stats.OldestTimestamp.Value.ToString("g")
                        : "none");
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
