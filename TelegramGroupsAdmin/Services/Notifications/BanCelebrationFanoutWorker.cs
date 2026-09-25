using TelegramGroupsAdmin.Core.Extensions;

namespace TelegramGroupsAdmin.Services.Notifications;

/// <summary>
/// Single reader of the fan-out queue. One celebration at a time, each in its own DI scope;
/// a failed item is logged and never stops the loop. Stops only on host shutdown.
/// </summary>
internal sealed class BanCelebrationFanoutWorker(
    IBanCelebrationFanoutQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<BanCelebrationFanoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<BanCelebrationFanoutProcessor>();
                    await processor.ProcessAsync(item, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ban celebration fan-out failed for {Chat}", item.Chat.ToLogDebug());
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown
        }
    }
}
