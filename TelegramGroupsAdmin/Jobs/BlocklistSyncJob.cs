using System.Diagnostics;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Job logic for syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
public class BlocklistSyncJobLogic
{
    private readonly IBlocklistSyncService _syncService;
    private readonly ILogger<BlocklistSyncJobLogic> _logger;

    public BlocklistSyncJobLogic(
        IBlocklistSyncService syncService,
        ILogger<BlocklistSyncJobLogic> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task ExecuteAsync(BlocklistSyncJobPayload payload, CancellationToken cancellationToken)
    {
        const string jobName = "BlocklistSync";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        if (payload == null)
        {
            _logger.LogWarning("BlocklistSyncJobLogic received null payload, skipping");
            return;
        }

        try
        {
            try
            {
                _logger.LogInformation("BlocklistSyncJobLogic started with payload: SubscriptionId={SubscriptionId}, ChatId={ChatId}, ForceRebuild={ForceRebuild}",
                    payload.SubscriptionId, payload.ChatId, payload.ForceRebuild);

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

                _logger.LogInformation("BlocklistSyncJobLogic completed successfully");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BlocklistSyncJobLogic failed");
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
