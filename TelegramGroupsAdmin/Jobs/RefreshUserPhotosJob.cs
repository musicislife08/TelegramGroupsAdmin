using System.Diagnostics;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Services.Media;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Nightly job logic to refresh user photos for all active users (seen in last 30 days)
/// Queues refetch requests for smart cache invalidation
/// </summary>
public class RefreshUserPhotosJobLogic
{
    private readonly ILogger<RefreshUserPhotosJobLogic> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RefreshUserPhotosJobLogic(
        ILogger<RefreshUserPhotosJobLogic> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task ExecuteAsync(RefreshUserPhotosPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "RefreshUserPhotos";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            try
            {
                if (payload == null)
                {
                    _logger.LogError("RefreshUserPhotosJobLogic received null payload");
                    return;
                }

                _logger.LogInformation("Starting user photo refresh for users active in last {Days} days", payload.DaysBack);

                using var scope = _scopeFactory.CreateScope();
                var userRepo = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
                var queueService = scope.ServiceProvider.GetRequiredService<IMediaRefetchQueueService>();

                // Get all users active in the last N days
                var activeUsers = await userRepo.GetActiveUsersAsync(payload.DaysBack, cancellationToken);
                _logger.LogInformation("Found {Count} active users to refresh", activeUsers.Count);

                var queuedCount = 0;
                foreach (var user in activeUsers)
                {
                    // Enqueue photo refetch (deduplication handled by queue service)
                    var wasQueued = await queueService.EnqueueUserPhotoAsync(user.TelegramUserId);
                    if (wasQueued)
                    {
                        queuedCount++;
                    }
                }

                _logger.LogInformation("Queued {QueuedCount}/{TotalCount} user photo refetch requests", queuedCount, activeUsers.Count);

                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing user photos");
                throw; // Re-throw for retry logic and exception recording
            }
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

            // Record metrics (using TagList to avoid boxing/allocations)
            var tags = new TagList
            {
                { "job_name", jobName },
                { "status", success ? "success" : "failure" }
            };

            TelemetryConstants.JobExecutions.Add(1, tags);
            TelemetryConstants.JobDuration.Record(elapsedMs, new TagList { { "job_name", jobName } });
        }
    }
}
