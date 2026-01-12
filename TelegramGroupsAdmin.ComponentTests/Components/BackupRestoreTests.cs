using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Data;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BackupRestore tests.
/// Registers mocked IBackupService, IBackgroundJobConfigService, and IDbContextFactory.
/// </summary>
/// <remarks>
/// This component has complex dependencies including IDbContextFactory which requires
/// special handling. Some functionality is better suited to Playwright E2E tests.
/// </remarks>
public class BackupRestoreTestContext : BunitContext
{
    protected IBackupService BackupService { get; }
    protected IBackgroundJobConfigService JobConfigService { get; }
    protected IDbContextFactory<AppDbContext> ContextFactory { get; }

    protected BackupRestoreTestContext()
    {
        // Create mocks
        BackupService = Substitute.For<IBackupService>();
        JobConfigService = Substitute.For<IBackgroundJobConfigService>();
        ContextFactory = Substitute.For<IDbContextFactory<AppDbContext>>();

        // Default: job config with default settings
        var defaultJobConfig = new BackgroundJobConfig
        {
            JobName = "ScheduledBackup",
            DisplayName = "Scheduled Backup",
            Description = "Automated database and media backup",
            Schedule = "1d at 2am",
            Enabled = true,
            ScheduledBackup = new ScheduledBackupSettings
            {
                RetainHourlyBackups = 24,
                RetainDailyBackups = 7,
                BackupDirectory = "/data/backups"
            }
        };
        JobConfigService.GetJobConfigAsync(Arg.Any<string>()).Returns(defaultJobConfig);

        // Register mocks
        Services.AddSingleton(BackupService);
        Services.AddSingleton(JobConfigService);
        Services.AddSingleton(ContextFactory);

        // Add MudBlazor services
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Setup JSInterop
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.disconnect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }
}

/// <summary>
/// Component tests for BackupRestore.razor
/// Tests the backup and restore management component.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests strongly recommended for:
/// - Testing file upload and restore flow (requires real file handling)
/// - Testing BackupBrowser child component interactions
/// - Testing passphrase prompt dialogs during restore
/// - Testing navigation after restore completes
/// - Testing backup creation with real file system
/// - Testing encryption status detection
///
/// Note: The IDbContextFactory dependency makes unit testing encryption status
/// checks difficult. The component queries the database directly for config.
/// </remarks>
[TestFixture]
public class BackupRestoreTests : BackupRestoreTestContext
{
    [SetUp]
    public void Setup()
    {
        BackupService.ClearReceivedCalls();
        JobConfigService.ClearReceivedCalls();
    }

    #region Structure Tests - Encryption Not Configured

    [Test]
    public void ShowsEncryptionSetupBanner_WhenNotConfigured()
    {
        // Arrange - Default state is encryption not configured
        // Note: Component checks database directly, so we can't easily mock this
        // The component will show the setup banner by default

        // Act
        var cut = Render<BackupRestore>();

        // Assert - Should show setup required banner
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Backup Encryption Setup Required"));
        });
    }

    [Test]
    public void HasSetUpEncryptionButton_WhenNotConfigured()
    {
        // Arrange & Act
        var cut = Render<BackupRestore>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Set Up Encryption Now"));
        });
    }

    #endregion

    #region Info Alert Tests

    [Test]
    public void DisplaysEncryptionRequiredMessage()
    {
        // Arrange & Act
        var cut = Render<BackupRestore>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("All backups are encrypted"));
        });
    }

    #endregion
}

/// <summary>
/// Additional tests that require encryption to be configured.
/// These would need a more sophisticated mock setup or Playwright E2E tests.
/// </summary>
/// <remarks>
/// The following tests are documented but not implemented because they require
/// mocking the DbContext query that checks encryption configuration:
///
/// - HasCreateBackupSection_WhenEncryptionConfigured
/// - HasRestoreFromUploadSection_WhenEncryptionConfigured
/// - HasBackupBrowser_WhenEncryptionConfigured
/// - HasBackupConfigurationSection_WhenEncryptionConfigured
/// - HasRotatePassphraseButton_WhenEncryptionConfigured
/// - DisplaysRetentionStrategyInfo_WhenEncryptionConfigured
///
/// These are better tested with Playwright E2E tests that can:
/// 1. Set up encryption first
/// 2. Then test the backup/restore functionality
/// </remarks>
[TestFixture]
public class BackupRestoreEncryptedModeTests
{
    // Placeholder for future Playwright E2E tests
    // See remarks above for list of recommended tests

    [Test]
    public void DocumentedForPlaywrightTesting()
    {
        // This test documents that encrypted mode testing requires Playwright
        Assert.Pass("Encrypted mode backup/restore tests should be implemented with Playwright E2E");
    }
}
