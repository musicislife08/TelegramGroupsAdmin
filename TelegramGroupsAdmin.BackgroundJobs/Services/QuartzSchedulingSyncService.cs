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
    IQuartzScheduleSynchronizer synchronizer) : BackgroundService
{
    private readonly SemaphoreSlim _resyncSignal = new(0, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("QuartzSchedulingSyncService starting...");

        try
        {
            // Get the scheduler instance
            var scheduler = await schedulerFactory.GetScheduler(stoppingToken);

            // Ensure default job configs exist
            await jobConfigService.EnsureDefaultConfigsAsync(stoppingToken);

            // Register this service with BackgroundJobConfigService for live re-sync notifications
            if (jobConfigService is BackgroundJobConfigService configService)
            {
                configService.SetSyncService(this);
                logger.LogDebug("Registered with BackgroundJobConfigService for live config re-sync");
            }

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
                    // Block until TriggerResync() is called or cancellation requested
                    await _resyncSignal.WaitAsync(stoppingToken);

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

    /// <summary>
    /// Trigger immediate re-sync of job schedules from database.
    /// Called by BackgroundJobConfigService after configuration changes.
    /// </summary>
    public void TriggerResync()
    {
        // Release the semaphore to wake up the monitoring loop
        // Try-catch handles concurrent calls (semaphore maxCount=1)
        try
        {
            _resyncSignal.Release();
            logger.LogDebug("Config re-sync triggered");
        }
        catch (SemaphoreFullException)
        {
            // Resync already pending, no action needed
            logger.LogDebug("Resync already pending, ignoring duplicate trigger");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("QuartzSchedulingSyncService stopping...");
        await base.StopAsync(cancellationToken);
    }
}
