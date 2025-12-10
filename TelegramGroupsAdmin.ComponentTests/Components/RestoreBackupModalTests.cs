using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for RestoreBackupModal tests.
/// Registers mocked IBackupService.
/// </summary>
public class RestoreBackupModalTestContext : BunitContext
{
    protected IBackupService BackupService { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected RestoreBackupModalTestContext()
    {
        // Create mocks
        BackupService = Substitute.For<IBackupService>();

        // Register mocks
        Services.AddSingleton(BackupService);

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

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for RestoreBackupModal.razor
/// Tests the restore from backup dialog.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests strongly recommended for:
/// - Testing file upload functionality (IBrowserFile handling)
/// - Testing encrypted backup passphrase prompt flow
/// - Testing restore progress and navigation after completion
/// - Testing backup metadata display after file selection
/// - Testing error handling for invalid/corrupted backup files
/// </remarks>
[TestFixture]
public class RestoreBackupModalTests : RestoreBackupModalTestContext
{
    [SetUp]
    public void Setup()
    {
        BackupService.ClearReceivedCalls();
    }

    #region Helper Methods

    private async Task<IDialogReference> OpenDialogAsync()
    {
        var parameters = new DialogParameters<RestoreBackupModal>();
        return await DialogService.ShowAsync<RestoreBackupModal>("Restore System from Backup", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-content"));
        });
    }

    [Test]
    public void HasDialogActions()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog-actions"));
        });
    }

    [Test]
    public void DisplaysTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Restore System from Backup"));
        });
    }

    #endregion

    #region Warning Alert Tests

    [Test]
    public void DisplaysRestoreWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("restore all users, settings, and data"));
        });
    }

    [Test]
    public void DisplaysLoginWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("log in with credentials from the backup"));
        });
    }

    #endregion

    #region File Upload Tests

    [Test]
    public void HasSelectBackupFileButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Select Backup File"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void HasCancelButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });
    }

    [Test]
    public void HasRestoreButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Restore"));
        });
    }

    [Test]
    public void RestoreButtonDisabled_WhenNoFileSelected()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Restore button should be disabled when no metadata
        provider.WaitForAssertion(() =>
        {
            var restoreButton = provider.FindAll("button")
                .FirstOrDefault(b => b.TextContent.Trim() == "Restore");
            Assert.That(restoreButton, Is.Not.Null);
            Assert.That(restoreButton!.GetAttribute("disabled"), Is.Not.Null);
        });
    }

    #endregion

    #region Cancel Tests

    [Test]
    public void CancelButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });

        // Act - Click cancel button
        var cancelButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Cancel");
        cancelButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
