using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Thin background-worker shell that drives <see cref="IQuartzScheduleSynchronizer"/>:
/// performs an initial sync on startup, then waits on a signal for live config-change re-syncs.
/// All sync business logic lives in <see cref="IQuartzScheduleSynchronizer"/> so it can be
/// tested directly without standing up a host or BackgroundService lifecycle.
/// </summary>
public class QuartzSchedulingSyncService(
    ILogger<QuartzSchedulingSyncService> logger,
    ISchedulerFactory schedulerFactory,
    IBackgroundJobConfigService jobConfigService,
    IQuartzScheduleSynchronizer synchronizer,
    IScheduleResyncSignal resyncSignal) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QuartzSchedulingSyncService starting...");

        try
        {
            // Get the scheduler instance
            var scheduler = await schedulerFactory.GetScheduler(stoppingToken);

            // Ensure default job configs exist
            await jobConfigService.EnsureDefaultConfigsAsync(stoppingToken);

            // Perform initial sync on startup
            await synchronizer.SyncAsync(scheduler, stoppingToken);

            // Update NextRunAt for all jobs after initial sync
            await synchronizer.UpdateNextRunTimesAsync(scheduler, stoppingToken);

            logger.LogInformation("QuartzSchedulingSyncService initial sync complete - listening for config changes");

            // Wait for config change notifications (event-driven re-sync)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Block until a re-sync is requested or cancellation is requested
                    await resyncSignal.WaitAsync(stoppingToken);

                    logger.LogInformation("Config change detected - re-syncing job schedules");

                    // Re-sync all schedules
                    await synchronizer.SyncAsync(scheduler, stoppingToken);
                    await synchronizer.UpdateNextRunTimesAsync(scheduler, stoppingToken);

                    logger.LogInformation("Config re-sync complete");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during config re-sync - will retry on next change");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "QuartzSchedulingSyncService failed to start");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("QuartzSchedulingSyncService stopping...");
        await base.StopAsync(cancellationToken);
    }
}
