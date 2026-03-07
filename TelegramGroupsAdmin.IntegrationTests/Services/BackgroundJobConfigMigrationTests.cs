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
/// Integration tests for BackgroundJobConfigService settings format migration.
/// Validates that old Dictionary&lt;string, object&gt; Settings format is properly
/// migrated to new typed embedded settings properties.
/// </summary>
[TestFixture]
public class BackgroundJobConfigMigrationTests
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

    #region Migration Tests

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithOldSettingsFormat_MigratesToTypedProperties()
    {
        // Arrange - Insert old-format JSON with Settings dictionary
        var oldFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.DataCleanup}}": {
                    "JobName": "{{BackgroundJobNames.DataCleanup}}",
                    "DisplayName": "Data Cleanup",
                    "Description": "Delete expired messages",
                    "Enabled": true,
                    "Schedule": "every day",
                    "Settings": {
                        "MessageRetention": "14d",
                        "ReportRetention": "60d",
                        "CallbackContextRetention": "3d",
                        "WebNotificationRetention": "10d"
                    }
                },
                "{{BackgroundJobNames.ScheduledBackup}}": {
                    "JobName": "{{BackgroundJobNames.ScheduledBackup}}",
                    "DisplayName": "Scheduled Backups",
                    "Description": "Backup database",
                    "Enabled": false,
                    "Schedule": "every day at 2am",
                    "Settings": {
                        "RetainHourlyBackups": 12,
                        "RetainDailyBackups": 14,
                        "RetainWeeklyBackups": 8,
                        "RetainMonthlyBackups": 6,
                        "RetainYearlyBackups": 2,
                        "BackupDirectory": "/custom/backups"
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{oldFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act - Run migration via EnsureDefaultConfigsAsync
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert - Verify MessageCleanup was migrated to typed DataCleanup
        var messageCleanupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        Assert.That(messageCleanupJob, Is.Not.Null, "MessageCleanup job should exist after migration");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(messageCleanupJob!.DataCleanup, Is.Not.Null, "DataCleanup typed settings should be populated");
            Assert.That(messageCleanupJob.DataCleanup!.MessageRetention, Is.EqualTo("14d"));
            Assert.That(messageCleanupJob.DataCleanup.ReportRetention, Is.EqualTo("60d"));
            Assert.That(messageCleanupJob.DataCleanup.CallbackContextRetention, Is.EqualTo("3d"));
            Assert.That(messageCleanupJob.DataCleanup.WebNotificationRetention, Is.EqualTo("10d"));
        }

        // Assert - Verify ScheduledBackup was migrated to typed ScheduledBackup
        var backupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.ScheduledBackup);
        Assert.That(backupJob, Is.Not.Null, "ScheduledBackup job should exist after migration");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupJob!.ScheduledBackup, Is.Not.Null, "ScheduledBackup typed settings should be populated");
            Assert.That(backupJob.ScheduledBackup!.RetainHourlyBackups, Is.EqualTo(12));
            Assert.That(backupJob.ScheduledBackup.RetainDailyBackups, Is.EqualTo(14));
            Assert.That(backupJob.ScheduledBackup.RetainWeeklyBackups, Is.EqualTo(8));
            Assert.That(backupJob.ScheduledBackup.RetainMonthlyBackups, Is.EqualTo(6));
            Assert.That(backupJob.ScheduledBackup.RetainYearlyBackups, Is.EqualTo(2));
            Assert.That(backupJob.ScheduledBackup.BackupDirectory, Is.EqualTo("/custom/backups"));
        }
    }

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithNewTypedFormat_SkipsMigration()
    {
        // Arrange - Insert new-format JSON (already has typed properties)
        var newFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.DataCleanup}}": {
                    "JobName": "{{BackgroundJobNames.DataCleanup}}",
                    "DisplayName": "Data Cleanup",
                    "Description": "Delete expired messages",
                    "Enabled": true,
                    "Schedule": "every day",
                    "DataCleanup": {
                        "MessageRetention": "45d",
                        "ReportRetention": "90d",
                        "CallbackContextRetention": "14d",
                        "WebNotificationRetention": "21d"
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{newFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act - Run "migration" (should skip since already in new format)
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert - Values should be unchanged (migration didn't corrupt them)
        var job = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        Assert.That(job, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job!.DataCleanup, Is.Not.Null);
            Assert.That(job.DataCleanup!.MessageRetention, Is.EqualTo("45d"));
            Assert.That(job.DataCleanup.ReportRetention, Is.EqualTo("90d"));
            Assert.That(job.DataCleanup.CallbackContextRetention, Is.EqualTo("14d"));
            Assert.That(job.DataCleanup.WebNotificationRetention, Is.EqualTo("21d"));
        }
    }

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithMixedFormat_MigratesOnlyOldJobs()
    {
        // Arrange - Mix of old and new format jobs
        var mixedFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.DataCleanup}}": {
                    "JobName": "{{BackgroundJobNames.DataCleanup}}",
                    "DisplayName": "Data Cleanup",
                    "Description": "Delete expired messages",
                    "Enabled": true,
                    "Schedule": "every day",
                    "DataCleanup": {
                        "MessageRetention": "30d",
                        "ReportRetention": "30d",
                        "CallbackContextRetention": "7d",
                        "WebNotificationRetention": "7d"
                    }
                },
                "{{BackgroundJobNames.ScheduledBackup}}": {
                    "JobName": "{{BackgroundJobNames.ScheduledBackup}}",
                    "DisplayName": "Scheduled Backups",
                    "Description": "Backup database",
                    "Enabled": true,
                    "Schedule": "every hour",
                    "Settings": {
                        "RetainHourlyBackups": 48,
                        "BackupDirectory": "/mixed/backups"
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{mixedFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert - MessageCleanup should be unchanged (already typed)
        var cleanupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        Assert.That(cleanupJob!.DataCleanup!.MessageRetention, Is.EqualTo("30d"));

        // Assert - ScheduledBackup should be migrated
        var backupJob = await _configService.GetJobConfigAsync(BackgroundJobNames.ScheduledBackup);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backupJob!.ScheduledBackup, Is.Not.Null);
            Assert.That(backupJob.ScheduledBackup!.RetainHourlyBackups, Is.EqualTo(48));
            Assert.That(backupJob.ScheduledBackup.BackupDirectory, Is.EqualTo("/mixed/backups"));
            // Defaults should be applied for missing settings
            Assert.That(backupJob.ScheduledBackup.RetainDailyBackups, Is.EqualTo(7)); // default
        }
    }

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

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithDatabaseMaintenanceOldFormat_MigratesCorrectly()
    {
        // Arrange - Old format for DatabaseMaintenance job
        var oldFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.DatabaseMaintenance}}": {
                    "JobName": "{{BackgroundJobNames.DatabaseMaintenance}}",
                    "DisplayName": "Database Maintenance",
                    "Description": "Run VACUUM and ANALYZE",
                    "Enabled": true,
                    "Schedule": "every week",
                    "Settings": {
                        "RunVacuum": false,
                        "RunAnalyze": true
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{oldFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert
        var maintenanceJob = await _configService.GetJobConfigAsync(BackgroundJobNames.DatabaseMaintenance);
        Assert.That(maintenanceJob, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(maintenanceJob!.DatabaseMaintenance, Is.Not.Null);
            Assert.That(maintenanceJob.DatabaseMaintenance!.RunVacuum, Is.False);
            Assert.That(maintenanceJob.DatabaseMaintenance.RunAnalyze, Is.True);
        }
    }

    [Test]
    public async Task EnsureDefaultConfigsAsync_WithUserPhotoRefreshOldFormat_MigratesCorrectly()
    {
        // Arrange - Old format for UserPhotoRefresh job
        var oldFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.UserPhotoRefresh}}": {
                    "JobName": "{{BackgroundJobNames.UserPhotoRefresh}}",
                    "DisplayName": "User Photo Refresh",
                    "Description": "Refresh user photos",
                    "Enabled": true,
                    "Schedule": "every day at 3am",
                    "Settings": {
                        "DaysBack": 14
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{oldFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act
        await _configService!.EnsureDefaultConfigsAsync();

        // Assert
        var photoJob = await _configService.GetJobConfigAsync(BackgroundJobNames.UserPhotoRefresh);
        Assert.That(photoJob, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(photoJob!.UserPhotoRefresh, Is.Not.Null);
            Assert.That(photoJob.UserPhotoRefresh!.DaysBack, Is.EqualTo(14));
        }
    }

    [Test]
    public async Task EnsureDefaultConfigsAsync_MigrationIsIdempotent()
    {
        // Arrange - Insert old format
        var oldFormatJson = $$"""
        {
            "Jobs": {
                "{{BackgroundJobNames.DataCleanup}}": {
                    "JobName": "{{BackgroundJobNames.DataCleanup}}",
                    "DisplayName": "Data Cleanup",
                    "Description": "Delete expired messages",
                    "Enabled": true,
                    "Schedule": "every day",
                    "Settings": {
                        "MessageRetention": "7d"
                    }
                }
            }
        }
        """;

        await _testHelper!.ExecuteSqlAsync($@"
            INSERT INTO configs (chat_id, background_jobs_config, created_at)
            VALUES (0, '{oldFormatJson.Replace("'", "''")}', NOW())
        ");

        // Act - Run migration twice
        await _configService!.EnsureDefaultConfigsAsync();
        await _configService.EnsureDefaultConfigsAsync();

        // Assert - Values should still be correct after running twice
        var job = await _configService.GetJobConfigAsync(BackgroundJobNames.DataCleanup);
        Assert.That(job!.DataCleanup!.MessageRetention, Is.EqualTo("7d"));
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
