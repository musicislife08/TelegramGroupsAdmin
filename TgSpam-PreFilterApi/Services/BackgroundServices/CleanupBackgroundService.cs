using Microsoft.Extensions.Options;
using TgSpam_PreFilterApi.Configuration;
using TgSpam_PreFilterApi.Data;

namespace TgSpam_PreFilterApi.Services.BackgroundServices;

public class CleanupBackgroundService : BackgroundService
{
    private readonly MessageHistoryRepository _repository;
    private readonly MessageHistoryOptions _options;
    private readonly ILogger<CleanupBackgroundService> _logger;

    public CleanupBackgroundService(
        MessageHistoryRepository repository,
        IOptions<MessageHistoryOptions> options,
        ILogger<CleanupBackgroundService> logger)
    {
        _repository = repository;
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

                var deleted = await _repository.CleanupExpiredAsync();
                var stats = await _repository.GetStatsAsync();

                _logger.LogInformation(
                    "Cleanup complete: {Deleted} expired messages removed. Stats: {Messages} messages, {Users} users, {Photos} photos, oldest: {Oldest}",
                    deleted,
                    stats.TotalMessages,
                    stats.UniqueUsers,
                    stats.PhotoCount,
                    stats.OldestTimestamp.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(stats.OldestTimestamp.Value).ToString("g")
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
