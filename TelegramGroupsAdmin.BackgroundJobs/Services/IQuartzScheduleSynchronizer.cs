using Quartz;

namespace TelegramGroupsAdmin.BackgroundJobs.Services;

/// <summary>
/// Reconciles Quartz scheduler state with database-driven job configuration.
/// Pulled out of QuartzSchedulingSyncService so the sync logic can be unit-/integration-tested
/// independently of the background worker loop. The worker is now a thin orchestration shell;
/// all business logic lives here.
/// </summary>
public interface IQuartzScheduleSynchronizer
{
    /// <summary>
    /// Performs one full reconciliation pass against the supplied scheduler:
    /// creates/updates triggers for enabled jobs, removes triggers for disabled jobs,
    /// and deletes orphaned Quartz jobs whose identities are no longer present in
    /// <see cref="TelegramGroupsAdmin.Core.BackgroundJobs.BackgroundJobNames"/>.
    /// </summary>
    Task SyncAsync(IScheduler scheduler, CancellationToken cancellationToken);

    /// <summary>
    /// Updates each job config's NextRunAt by reading the corresponding trigger's
    /// next fire time from the supplied scheduler. Run after <see cref="SyncAsync"/>.
    /// </summary>
    Task UpdateNextRunTimesAsync(IScheduler scheduler, CancellationToken cancellationToken);
}
