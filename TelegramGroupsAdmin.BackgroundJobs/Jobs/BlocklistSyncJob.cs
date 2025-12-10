using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quartz;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

namespace TelegramGroupsAdmin.BackgroundJobs.Jobs;

/// <summary>
/// Job logic for syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
[DisallowConcurrentExecution]
public class BlocklistSyncJob : IJob
{
    private readonly IBlocklistSyncService _syncService;
    private readonly ILogger<BlocklistSyncJob> _logger;

    public BlocklistSyncJob(
        IBlocklistSyncService syncService,
        ILogger<BlocklistSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Execute blocklist sync (Quartz.NET entry point)
    /// </summary>
    public async Task Execute(IJobExecutionContext context)
    {
        // Extract payload from job data map (deserialize from JSON string)
        // Scheduled triggers don't have payloads, manual triggers do
        BlocklistSyncJobPayload payload;

        if (context.JobDetail.JobDataMap.ContainsKey(JobDataKeys.PayloadJson))
        {
            // Manual trigger - deserialize provided payload
            var payloadJson = context.JobDetail.JobDataMap.GetString(JobDataKeys.PayloadJson)!;
            payload = JsonSerializer.Deserialize<BlocklistSyncJobPayload>(payloadJson)
                ?? throw new InvalidOperationException("Failed to deserialize BlocklistSyncJobPayload");
        }
        else
        {
            // Scheduled trigger - use default payload (sync all enabled subscriptions, global scope)
            payload = new BlocklistSyncJobPayload(
                SubscriptionId: null,
                ChatId: 0,
                ForceRebuild: false
            );
        }

        await ExecuteAsync(payload, context.CancellationToken);
    }

    /// <summary>
    /// Execute blocklist sync (business logic)
    /// </summary>
    private async Task ExecuteAsync(BlocklistSyncJobPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "BlocklistSync";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        try
        {
            _logger.LogInformation("BlocklistSyncJob started with payload: SubscriptionId={SubscriptionId}, ChatId={ChatId}, ForceRebuild={ForceRebuild}",
                payload.SubscriptionId, payload.ChatId, payload.ForceRebuild);

            try
            {

                if (payload.ForceRebuild)
                {
                    // Full cache rebuild
                    await _syncService.RebuildCacheAsync(payload.ChatId, cancellationToken);
                }
                else if (payload.SubscriptionId.HasValue)
                {
                    // Sync specific subscription
                    await _syncService.SyncSubscriptionAsync(payload.SubscriptionId.Value, cancellationToken);
                }
                else
                {
                    // Sync all enabled subscriptions
                    await _syncService.SyncAllAsync(cancellationToken);
                }

                _logger.LogInformation("BlocklistSyncJob completed successfully");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BlocklistSyncJob failed");
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
