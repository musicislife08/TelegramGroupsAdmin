using System.Diagnostics;
using TelegramGroupsAdmin.Core.Telemetry;
using TelegramGroupsAdmin.Telegram.Abstractions.Jobs;
using TelegramGroupsAdmin.ContentDetection.Services.Blocklists;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// TickerQ job for syncing external blocklists
/// Phase 4.13: URL Filtering
/// </summary>
public class BlocklistSyncJob
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

    [TickerFunction(functionName: "BlocklistSync")]
    public async Task ExecuteAsync(TickerFunctionContext<BlocklistSyncJobPayload> context, CancellationToken cancellationToken)
    {
        const string jobName = "BlocklistSync";
        var startTimestamp = Stopwatch.GetTimestamp();
        var success = false;

        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogWarning("BlocklistSyncJob received null payload, skipping");
            return;
        }

        try
        {
            try
            {
                _logger.LogInformation("BlocklistSyncJob started with payload: SubscriptionId={SubscriptionId}, ChatId={ChatId}, ForceRebuild={ForceRebuild}",
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

                _logger.LogInformation("BlocklistSyncJob completed successfully");
                success = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BlocklistSyncJob failed");
                throw; // Re-throw for TickerQ retry mechanism
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
