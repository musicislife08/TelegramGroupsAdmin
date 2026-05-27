using System.Collections.Specialized;
using HumanCron.Quartz.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Services.BackgroundJobs;

/// <summary>
/// Integration tests for <see cref="QuartzScheduleSynchronizer"/>. Exercises the orphan-cleanup
/// behavior introduced for #459: the synchronizer must use BackgroundJobNames.AllRegisteredNames
/// (CLR-registered set), not the DB-config keyset, when deciding which Quartz jobs are orphans.
/// </summary>
/// <remarks>
/// NonParallelizable because Quartz's static LogProvider caches the first ILoggerFactory it sees
/// process-wide; running two scheduler bootstraps concurrently races on shared state. Using
/// NullLoggerFactory throughout eliminates the dispose-between-tests hazard.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class QuartzScheduleSynchronizerOrphanCleanupTests
{
    private IScheduler _scheduler = null!;
    private QuartzScheduleSynchronizer _synchronizer = null!;

    [SetUp]
    public async Task SetUp()
    {
        // Unique scheduler name per fixture instance to avoid cross-test scheduler collision.
        var schedulerName = $"TestScheduler_{Guid.NewGuid():N}";

        // Build a stand-alone RAM-store Quartz scheduler via DirectSchedulerFactory.
        // No DI/host needed — the synchronizer takes the scheduler as a method parameter.
        var schedulerProps = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = schedulerName,
            ["quartz.scheduler.instanceId"] = "AUTO",
            ["quartz.threadPool.type"] = "Quartz.Simpl.DefaultThreadPool, Quartz",
            ["quartz.threadPool.maxConcurrency"] = "1",
            ["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz",
        };
        var factory = new StdSchedulerFactory(schedulerProps);
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();

        // Seed all CLR-registered jobs into the scheduler so orphan detection has something to find
        // (this mirrors what AddBackgroundJobs.RegisterJobs does in production).
        foreach (var name in BackgroundJobNames.AllRegisteredNames)
        {
            var job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(name)
                .StoreDurably()
                .Build();
            await _scheduler.AddJob(job, replace: true);
        }

        // Mocked IBackgroundJobConfigService — returns an empty job map. This simulates the
        // DB-config keyset being a strict subset of all registered jobs, which is exactly the
        // scenario that exposed the bug: passing allJobs.Keys (empty) to RemoveOrphanedJobsAsync
        // would delete every Quartz-registered job.
        var mockConfigService = Substitute.For<IBackgroundJobConfigService>();
        mockConfigService
            .GetAllJobsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, BackgroundJobConfig>());

        var mockScheduleConverter = Substitute.For<IQuartzScheduleConverter>();

        _synchronizer = new QuartzScheduleSynchronizer(
            NullLogger<QuartzScheduleSynchronizer>.Instance,
            mockConfigService,
            mockScheduleConverter);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_scheduler is { IsStarted: true })
            await _scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task SyncAsync_DoesNotDeleteAdHocJobs()
    {
        // Pre-condition (from SetUp): all CLR-registered ad-hoc jobs are present in the scheduler.

        await _synchronizer.SyncAsync(_scheduler, CancellationToken.None);

        var allKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
        var jobNames = allKeys.Select(k => k.Name).ToHashSet();

        Assert.That(jobNames, Does.Contain(BackgroundJobNames.DeleteMessage));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.FileScan));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.WelcomeTimeout));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.ProfileScan));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.TempbanExpiry));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.FetchUserPhoto));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.DeleteUserMessages));
        Assert.That(jobNames, Does.Contain(BackgroundJobNames.RotateBackupPassphrase));
    }

    [Test]
    public async Task SyncAsync_DeletesGenuineOrphans()
    {
        // Seed a ghost job that does not exist in BackgroundJobNames.
        var ghostKey = new JobKey("GhostJobThatNoLongerExists");
        await _scheduler.AddJob(
            JobBuilder.Create<NoOpJob>().WithIdentity(ghostKey).StoreDurably().Build(),
            replace: true);

        Assert.That(await _scheduler.CheckExists(ghostKey), Is.True);

        await _synchronizer.SyncAsync(_scheduler, CancellationToken.None);

        Assert.That(await _scheduler.CheckExists(ghostKey), Is.False);
    }

    private sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
