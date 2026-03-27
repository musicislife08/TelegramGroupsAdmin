using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

[TestFixture]
public class DataCleanupJobTests
{
    private IServiceScopeFactory _scopeFactory = null!;
    private IServiceScope _scope = null!;
    private IServiceProvider _serviceProvider = null!;
    private IBackgroundJobConfigService _configService = null!;
    private IMessageHistoryRepository _messageHistoryRepo = null!;
    private IReportsRepository _reportsRepo = null!;
    private IReportCallbackContextRepository _callbackContextRepo = null!;
    private IWebNotificationRepository _notificationRepo = null!;
    private IFileScanResultRepository _fileScanResultRepo = null!;
    private IJobExecutionContext _jobContext = null!;
    private DataCleanupJob _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeFactory = Substitute.For<IServiceScopeFactory>();
        _serviceProvider = Substitute.For<IServiceProvider>();
        _configService = Substitute.For<IBackgroundJobConfigService>();
        _messageHistoryRepo = Substitute.For<IMessageHistoryRepository>();
        _reportsRepo = Substitute.For<IReportsRepository>();
        _callbackContextRepo = Substitute.For<IReportCallbackContextRepository>();
        _notificationRepo = Substitute.For<IWebNotificationRepository>();
        _fileScanResultRepo = Substitute.For<IFileScanResultRepository>();

        _scope = Substitute.For<IServiceScope>();
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);

        _serviceProvider.GetService(typeof(IBackgroundJobConfigService)).Returns(_configService);
        _serviceProvider.GetService(typeof(IMessageHistoryRepository)).Returns(_messageHistoryRepo);
        _serviceProvider.GetService(typeof(IReportsRepository)).Returns(_reportsRepo);
        _serviceProvider.GetService(typeof(IReportCallbackContextRepository)).Returns(_callbackContextRepo);
        _serviceProvider.GetService(typeof(IWebNotificationRepository)).Returns(_notificationRepo);
        _serviceProvider.GetService(typeof(IFileScanResultRepository)).Returns(_fileScanResultRepo);

        // Default: return empty config (use defaults)
        _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup)
            .Returns(Task.FromResult<BackgroundJobConfig?>(null));

        // Default: message cleanup returns empty result
        _messageHistoryRepo.CleanupExpiredAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new MessageCleanupResult(0, [], [], 0, 0, 0, null));

        var appOptions = Options.Create(new AppOptions { DataPath = "/data" });
        var logger = Substitute.For<ILogger<DataCleanupJob>>();

        _jobContext = Substitute.For<IJobExecutionContext>();
        _jobContext.CancellationToken.Returns(CancellationToken.None);

        _sut = new DataCleanupJob(_scopeFactory, appOptions, logger, new JobMetrics());
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task Execute_CallsFileScanResultCleanup_WithDefaultRetention()
    {
        // Act
        await _sut.Execute(_jobContext);

        // Assert — file scan result cleanup is called with 24h default retention
        await _fileScanResultRepo.Received(1).CleanupExpiredResultsAsync(
            TimeSpan.FromHours(24),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_CallsFileScanResultCleanup_WithConfiguredRetention()
    {
        // Arrange — configure 48h retention
        var settings = new DataCleanupSettings { FileScanResultRetention = "48h" };
        var config = new BackgroundJobConfig
        {
            JobName = BackgroundJobNames.DataCleanup,
            DisplayName = "Data Cleanup",
            Description = "Test",
            Schedule = "1d",
            DataCleanup = settings
        };
        _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup)
            .Returns(Task.FromResult<BackgroundJobConfig?>(config));

        // Act
        await _sut.Execute(_jobContext);

        // Assert — uses the configured 48h retention
        await _fileScanResultRepo.Received(1).CleanupExpiredResultsAsync(
            TimeSpan.FromHours(48),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_CallsAllFiveCleanupSteps()
    {
        // Act
        await _sut.Execute(_jobContext);

        // Assert — all 5 cleanup steps are called
        await _messageHistoryRepo.Received(1).CleanupExpiredAsync(
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _reportsRepo.Received(1).DeleteOldReportsAsync(
            Arg.Any<DateTimeOffset>(), type: null, Arg.Any<CancellationToken>());
        await _callbackContextRepo.Received(1).DeleteExpiredAsync(
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _notificationRepo.Received(1).DeleteOldReadNotificationsAsync(
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await _fileScanResultRepo.Received(1).CleanupExpiredResultsAsync(
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
