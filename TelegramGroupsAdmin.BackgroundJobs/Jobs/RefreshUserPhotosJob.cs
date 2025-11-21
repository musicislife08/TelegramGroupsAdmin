using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Services.Media;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Nightly job to refresh user photos for all active users (seen in last 30 days)
/// Queues refetch requests for smart cache invalidation
/// </summary>
public class RefreshUserPhotosJob : IJob
{
    private readonly ILogger<RefreshUserPhotosJob> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public RefreshUserPhotosJob(
        ILogger<RefreshUserPhotosJob> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Execute user photo refresh (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        // Scheduled triggers don't have payloads, manual triggers do
        RefreshUserPhotosPayload payload;

        if (context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.PayloadJson))
        {
            // Manual trigger - deserialize provided payload
            var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)!;
            payload = JsonSerializer.Deserialize<RefreshUserPhotosPayload>(payloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize RefreshUserPhotosPayload");
        }
        else
        {
            // Scheduled trigger - use default payload (30 days lookback)
            payload = new RefreshUserPhotosPayload { DaysBack = 30 };
        }

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute user photo refresh (business logic)
    /// </summary>
    private async Task ExecuteAsync(RefreshUserPhotosPayload payload, CancellationToken cancellationToken)
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
                    _logger.LogError("RefreshUserPhotosJob received null payload");
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
