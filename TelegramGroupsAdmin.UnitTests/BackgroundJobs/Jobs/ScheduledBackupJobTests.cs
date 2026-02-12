using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.JobPayloads;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

/// <summary>
/// Unit tests for ScheduledBackupJob.
/// Verifies the job reads settings from database config on scheduled triggers,
/// uses payload settings on manual triggers, and falls back gracefully on errors.
/// These tests would have caught the bug where the job ignored DB settings
/// and used a relative path fallback instead of the configured absolute path.
/// </summary>
[TestFixture]
public class ScheduledBackupJobTests
{
    private ILogger<ScheduledBackupJob> _logger = null!;
    private IServiceScopeFactory _scopeFactory = null!;
    private IServiceScope? _scope;
    private IServiceProvider _serviceProvider = null!;
    private IBackgroundJobConfigService _configService = null!;
    private IBackupService _backupService = null!;
    private IJobExecutionContext _jobContext = null!;

    private static readonly BackupResult DefaultBackupResult = new(
        "backup_2026-02-09_12-00-00.tar.gz",
        "/data/backups/backup_2026-02-09_12-00-00.tar.gz",
        1024 * 1024,
        0);

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<ScheduledBackupJob>>();
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _configService = Substitute.For<IBackgroundJobConfigService>();
        _backupService = Substitute.For<IBackupService>();

        // Wire scope factory chain
        _scope = Substitute.For<IServiceScope>();
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IBackgroundJobConfigService)).Returns(_configService);
        _serviceProvider.GetService(typeof(IBackupService)).Returns(_backupService);

        // Default backup service behavior
        _backupService.CreateBackupWithRetentionAsync(
                Arg.Any<string>(), Arg.Any<RetentionConfig>(), Arg.Any<CancellationToken>())
            .Returns(DefaultBackupResult);

        // Job context — empty data map by default (scheduled trigger)
        _jobContext = Substitute.For<IJobExecutionContext>();
        _jobContext.MergedJobDataMap.Returns(new JobDataMap());
        _jobContext.CancellationToken.Returns(CancellationToken.None);
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
    }

    [Test]
    public async Task Execute_ScheduledTrigger_ReadsSettingsFromDbConfig()
    {
        // Arrange — DB config has custom directory and retention
        SetupDbConfig(backupDir: "/custom/path", hourly: 48);

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act
        await job.Execute(_jobContext);

        // Assert — job read from DB config
        await _configService.Received(1)
            .GetJobConfigAsync(BackgroundJobNames.ScheduledBackup, Arg.Any<CancellationToken>());

        // Assert — backup service called with DB config values
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            "/custom/path",
            Arg.Is<RetentionConfig>(r => r.RetainHourlyBackups == 48),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScheduledTrigger_UsesDefaultDirectory_WhenDbConfigHasNoDirectory()
    {
        // Arrange — DB config has null BackupDirectory
        SetupDbConfig(backupDir: null);

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act
        await job.Execute(_jobContext);

        // Assert — uses absolute default path, NOT relative "data/backups"
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            BackupRetentionConstants.DefaultBackupDirectory,
            Arg.Any<RetentionConfig>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScheduledTrigger_UsesDbRetentionSettings()
    {
        // Arrange — DB config has non-default retention values
        SetupDbConfig(hourly: 10, daily: 3, weekly: 2, monthly: 6, yearly: 1);

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act
        await job.Execute(_jobContext);

        // Assert — all 5 retention values from DB config are passed through
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            Arg.Any<string>(),
            Arg.Is<RetentionConfig>(r =>
                r.RetainHourlyBackups == 10 &&
                r.RetainDailyBackups == 3 &&
                r.RetainWeeklyBackups == 2 &&
                r.RetainMonthlyBackups == 6 &&
                r.RetainYearlyBackups == 1),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ManualTrigger_UsesPayloadSettings()
    {
        // Arrange — payload in job data map (manual trigger)
        var payload = new ScheduledBackupPayload
        {
            BackupDirectory = "/manual/path",
            RetainHourlyBackups = 99,
            RetainDailyBackups = 14,
            RetainWeeklyBackups = 8,
            RetainMonthlyBackups = 24,
            RetainYearlyBackups = 5
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        _jobContext.MergedJobDataMap.Returns(new JobDataMap
        {
            { JobDataKeys.PayloadJson, payloadJson }
        });

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act
        await job.Execute(_jobContext);

        // Assert — DB config NOT consulted
        await _configService.DidNotReceive()
            .GetJobConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Assert — payload values used directly
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            "/manual/path",
            Arg.Is<RetentionConfig>(r =>
                r.RetainHourlyBackups == 99 &&
                r.RetainDailyBackups == 14),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScheduledTrigger_FallsBackToDefaults_WhenDbConfigReadFails()
    {
        // Arrange — config service throws
        _configService.GetJobConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB connection failed"));

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act — should NOT throw (graceful fallback)
        await job.Execute(_jobContext);

        // Assert — backup still runs with default absolute path and default retention
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            BackupRetentionConstants.DefaultBackupDirectory,
            Arg.Is<RetentionConfig>(r =>
                r.RetainHourlyBackups == 24 &&
                r.RetainDailyBackups == 7 &&
                r.RetainWeeklyBackups == 4 &&
                r.RetainMonthlyBackups == 12 &&
                r.RetainYearlyBackups == 3),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScheduledTrigger_FallsBackToDefaults_WhenNoJobConfigExists()
    {
        // Arrange — config service returns null (job not configured in DB)
        _configService.GetJobConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BackgroundJobConfig?)null);

        var job = new ScheduledBackupJob(_logger, _scopeFactory);

        // Act
        await job.Execute(_jobContext);

        // Assert — default directory and default retention
        await _backupService.Received(1).CreateBackupWithRetentionAsync(
            BackupRetentionConstants.DefaultBackupDirectory,
            Arg.Is<RetentionConfig>(r =>
                r.RetainHourlyBackups == 24 &&
                r.RetainDailyBackups == 7 &&
                r.RetainWeeklyBackups == 4 &&
                r.RetainMonthlyBackups == 12 &&
                r.RetainYearlyBackups == 3),
            Arg.Any<CancellationToken>());
    }

    #region Helpers

    private void SetupDbConfig(
        string? backupDir = "/data/backups",
        int hourly = 24,
        int daily = 7,
        int weekly = 4,
        int monthly = 12,
        int yearly = 3)
    {
        var config = new BackgroundJobConfig
        {
            JobName = BackgroundJobNames.ScheduledBackup,
            DisplayName = "Scheduled Backups", // Matches default config factory
            Description = "Automatically backup database on a schedule",
            Schedule = "every day at 2am",
            ScheduledBackup = new ScheduledBackupSettings
            {
                BackupDirectory = backupDir,
                RetainHourlyBackups = hourly,
                RetainDailyBackups = daily,
                RetainWeeklyBackups = weekly,
                RetainMonthlyBackups = monthly,
                RetainYearlyBackups = yearly
            }
        };

        _configService.GetJobConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(config);
    }

    #endregion
}
