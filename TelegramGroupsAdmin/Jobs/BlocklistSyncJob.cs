using Microsoft.Extensions.Logging;
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
        var payload = context.Request;
        if (payload == null)
        {
            _logger.LogWarning("BlocklistSyncJob received null payload, skipping");
            return;
        }

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BlocklistSyncJob failed");
            throw; // Re-throw for TickerQ retry mechanism
        }
    }
}
