using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Services.Media;
using TelegramGroupsAdmin.Telegram.Abstractions;
using TelegramGroupsAdmin.Telegram.Repositories;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace TelegramGroupsAdmin.Jobs;

/// <summary>
/// Nightly job to refresh user photos for all active users (seen in last 30 days)
/// Queues refetch requests for smart cache invalidation
/// </summary>
public class RefreshUserPhotosJob
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

    [TickerFunction(functionName: "refresh_user_photos")]
    public async Task ExecuteAsync(TickerFunctionContext<RefreshUserPhotosPayload> context, CancellationToken cancellationToken)
    {
        try
        {
            var payload = context.Request;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing user photos");
            throw; // Re-throw for TickerQ retry logic
        }
    }
}
