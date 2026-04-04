using HumanCron.Quartz.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.Services;

/// <summary>
/// Integration tests for BackgroundJobConfigService settings persistence and default creation.
/// Validates that typed settings are correctly serialized/deserialized via PostgreSQL JSONB.
/// </summary>
[TestFixture]
public class BackgroundJobConfigPersistenceTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;
    private IBackgroundJobConfigService? _configService;

    [SetUp]
    public async Task SetUp()
    {
        // Create unique test database with migrations applied
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseAndApplyMigrationsAsync();

        // Set up dependency injection
        var services = new ServiceCollection();

        // Add DbContextFactory (BackgroundJobConfigService uses this pattern)
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        // Add logging (suppress debug logs in tests)
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        // Mock schedule converter (not needed for migration tests)
        var mockScheduleConverter = Substitute.For<IQuartzScheduleConverter>();
        services.AddSingleton(mockScheduleConverter);

        // Add BackgroundJobConfigService
        services.AddScoped<IBackgroundJobConfigService, BackgroundJobConfigService>();

        _serviceProvider = services.BuildServiceProvider();

        // Get service in a scope
        _scope = _serviceProvider.CreateScope();
        _configService = _scope.ServiceProvider.GetRequiredService<IBackgroundJobConfigService>();
    }

    [TearDown]
    public void TearDown()
    {
        _scope?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    #region Default Creation Tests

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithEmptyDatabase_CreatesDefaults()
    {
        // Arrange - No config record exists (fresh database)

        // Act
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert - Default jobs should be created with typed settings
        var allJobs = await _configService.GetAllJobsAsync();
        Assert.That(allJobs.Count, Is.GreaterThan(0), "Default jobs should be created");

        // Verify MessageCleanup has typed settings
        var cleanupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        Assert.That(cleanupJob, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cleanupJob!.DataCleanup, Is.Not.Null);
            Assert.That(cleanupJob.DataCleanup!.MessageRetention, Is.EqualTo(DataCleanupSettings.DefaultMessageRetentionString));
        }

        // Verify ScheduledBackup has typed settings
        var backupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.ScheduledBackup);
        Assert.That(backupJob, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupJob!.ScheduledBackup, Is.Not.Null);
            Assert.That(backupJob.ScheduledBackup!.BackupDirectory, Is.EqualTo("/data/backups")); // default
        }
    }

    #endregion

    #region Settings Persistence Tests

    [Test]
    public async Task UpdateJobConfigAsync_WithTypedSettings_PersistsCorrectly()
    {
        // Arrange - Create default config first
        await _configService!.EnsureDefaultConfigsAsync();

        // Update with custom typed settings
        var config = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        config!.DataCleanup = new DataCleanupSettings
        {
            MessageRetention = "60d",
            ReportRetention = "120d",
            CallbackContextRetention = "14d",
            WebNotificationRetention = "30d"
        };

        // Act
        await _configService.UpdateJobConfigAsync(BackgroundJobNames.DataCleanup, config);

        // Assert - Reload and verify persistence
        var reloaded = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloaded!.DataCleanup!.MessageRetention, Is.EqualTo("60d"));
            Assert.That(reloaded.DataCleanup.ReportRetention, Is.EqualTo("120d"));
            Assert.That(reloaded.DataCleanup.CallbackContextRetention, Is.EqualTo("14d"));
            Assert.That(reloaded.DataCleanup.WebNotificationRetention, Is.EqualTo("30d"));
        }
    }

    [Test]
    public async Task RoundTrip_TypedSettingsSerializeCorrectly()
    {
        // Arrange
        await _configService!.EnsureDefaultConfigsAsync();

        // Update backup settings
        var backupConfig = await _configService.GetJobConfigAsync(BackgroundJobNames.ScheduledBackup);
        backupConfig!.ScheduledBackup = new ScheduledBackupSettings
        {
            RetainHourlyBackups = 100,
            RetainDailyBackups = 50,
            RetainWeeklyBackups = 20,
            RetainMonthlyBackups = 24,
            RetainYearlyBackups = 10,
            BackupDirectory = "/test/roundtrip"
        };
        await _configService.UpdateJobConfigAsync(BackgroundJobNames.ScheduledBackup, backupConfig);

        // Act - Read directly from database to verify JSON structure
        var rawJson = await _testHelper!.ExecuteScalarAsync<string>(
            "SELECT background_jobs_config FROM configs WHERE chat_id = 0");

        // Assert - Verify JSON contains typed property, not Settings dictionary
        Assert.That(rawJson, Does.Contain("\"ScheduledBackup\""));
        Assert.That(rawJson, Does.Contain("\"RetainHourlyBackups\": 100")); // Note: serializer adds space after colon
        Assert.That(rawJson, Does.Not.Contain("\"Settings\""), "Old Settings dictionary should not be present");
    }

    #endregion
}
