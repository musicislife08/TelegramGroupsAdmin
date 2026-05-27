using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl.Matchers;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using HumanCron.Quartz.Abstractions;

namespace TelegramGroupsAdmin.IntegrationTests.Services.BackgroundJobs;

/// <summary>
/// Integration tests verifying that QuartzSchedulingSyncService.SyncSchedulesAsync
/// uses BackgroundJobNames.AllRegisteredNames as the authoritative "registered" set
/// for orphan detection, not the DB-config keyset.
/// </summary>
/// <remarks>
/// NonParallelizable because Quartz's static SchedulerRepository is a process-wide
/// singleton; running two tests that build/tear down schedulers concurrently races
/// on that shared state even with GUID-named schedulers.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class QuartzSchedulingSyncOrphanCleanupTests
{
    private ServiceProvider _serviceProvider = null!;
    private IScheduler _scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        var services = new ServiceCollection();

        // Use the static NullLoggerFactory so Quartz's static LogProvider doesn't capture
        // a per-test LoggerFactory that gets disposed between SetUp/TearDown cycles.
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();

        // Quartz with RAM store — no PostgreSQL needed for this test.
        // Unique scheduler name per test fixture instance to avoid cross-test
        // scheduler collision when tests run in parallel.
        var schedulerName = $"TestScheduler_{Guid.NewGuid():N}";
        services.AddQuartz(q =>
        {
            q.SchedulerName = schedulerName;
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 1);
            // RAM store is the default — no UsePersistentStore call
        });

        // Mock IBackgroundJobConfigService — returns empty job list.
        // This simulates the DB-config keyset being empty (subset of all registered jobs),
        // which is exactly the scenario that exposed the bug: passing allJobs.Keys (empty)
        // to RemoveOrphanedJobsAsync would delete every Quartz-registered job.
        var mockConfigService = Substitute.For<IBackgroundJobConfigService>();
        mockConfigService
            .GetAllJobsAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, TelegramGroupsAdmin.Core.Models.BackgroundJobConfig>());
        services.AddSingleton(mockConfigService);

        // Mock IQuartzScheduleConverter (not exercised by orphan path)
        services.AddSingleton(Substitute.For<IQuartzScheduleConverter>());

        // Register the service under test — as a plain singleton (not a hosted service)
        // so we control when SyncSchedulesAsync is called via SetSchedulerForTesting
        services.AddSingleton<QuartzSchedulingSyncService>();

        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false });

        // Start the Quartz scheduler manually (no hosted service needed)
        var factory = _serviceProvider.GetRequiredService<ISchedulerFactory>();
        _scheduler = await factory.GetScheduler();
        await _scheduler.Start();

        // Register all 16 CLR jobs with the RAM-store scheduler so orphan detection has
        // something to find (this mirrors what AddBackgroundJobs.RegisterJobs does in prod)
        foreach (var name in BackgroundJobNames.AllRegisteredNames)
        {
            var job = JobBuilder.Create<NoOpJob>()
                .WithIdentity(name)
                .StoreDurably()
                .Build();
            await _scheduler.AddJob(job, replace: true);
        }
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_scheduler is { IsStarted: true })
            await _scheduler.Shutdown(waitForJobsToComplete: false);
        await _serviceProvider.DisposeAsync();
    }

    [Test]
    public async Task SyncSchedulesAsync_DoesNotDeleteAdHocJobs()
    {
        var sync = _serviceProvider.GetRequiredService<QuartzSchedulingSyncService>();
        sync.SetSchedulerForTesting(_scheduler);

        await sync.SyncSchedulesAsync(CancellationToken.None);

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
    public async Task SyncSchedulesAsync_DeletesGenuineOrphans()
    {
        var sync = _serviceProvider.GetRequiredService<QuartzSchedulingSyncService>();
        sync.SetSchedulerForTesting(_scheduler);

        // Seed a ghost job that does not exist in BackgroundJobNames
        var ghostKey = new JobKey("GhostJobThatNoLongerExists");
        var ghost = JobBuilder.Create<NoOpJob>()
            .WithIdentity(ghostKey)
            .StoreDurably()
            .Build();
        await _scheduler.AddJob(ghost, replace: true);

        Assert.That(await _scheduler.CheckExists(ghostKey), Is.True);

        await sync.SyncSchedulesAsync(CancellationToken.None);

        Assert.That(await _scheduler.CheckExists(ghostKey), Is.False);
    }

    private sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
