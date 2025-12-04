using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BackupEncryptionSetupDialog tests.
/// Registers mocked IPassphraseManagementService.
/// </summary>
public class BackupEncryptionSetupDialogTestContext : BunitContext
{
    protected IPassphraseManagementService PassphraseService { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected BackupEncryptionSetupDialogTestContext()
    {
        // Create mocks
        PassphraseService = Substitute.For<IPassphraseManagementService>();

        // Register mocks
        Services.AddSingleton(PassphraseService);

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
        // Clipboard JS interop
        JSInterop.SetupVoid("navigator.clipboard.writeText", _ => true).SetVoidResult();
    }

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for BackupEncryptionSetupDialog.razor
/// Tests the dialog for setting up backup encryption.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests recommended for:
/// - Testing the full passphrase generation and copy flow
/// - Testing step transitions (Confirmation → DisplayPassphrase)
/// - Testing clipboard copy functionality
/// - Testing custom passphrase validation
/// </remarks>
[TestFixture]
public class BackupEncryptionSetupDialogTests : BackupEncryptionSetupDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        PassphraseService.ClearReceivedCalls();
    }

    #region Helper Methods

    private async Task<IDialogReference> OpenDialogAsync()
    {
        var parameters = new DialogParameters<BackupEncryptionSetupDialog>();
        return await DialogService.ShowAsync<BackupEncryptionSetupDialog>("Enable Backup Encryption", parameters);
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
            Assert.That(provider.Markup, Does.Contain("Enable Backup Encryption"));
        });
    }

    #endregion

    #region Confirmation Step Tests

    [Test]
    public void DisplaysWhatIsEncryptionInfo()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("What is backup encryption?"));
        });
    }

    [Test]
    public void DisplaysAes256Info()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("AES-256-GCM"));
        });
    }

    [Test]
    public void DisplaysImportantWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("ONCE"));
        });
    }

    [Test]
    public void HasUnderstandCheckbox()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("I understand that I need to save the passphrase"));
        });
    }

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
    public void HasGenerateButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Generate Passphrase"));
        });
    }

    #endregion

    #region Advanced Options Tests

    [Test]
    public void HasAdvancedOptionsSection()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Advanced Options"));
        });
    }

    [Test]
    public void HasCustomPassphraseOption()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Use custom passphrase"));
        });
    }

    #endregion

    #region Button State Tests

    [Test]
    public void GenerateButtonDisabled_WhenNotUnderstood()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Button should be disabled until checkbox is checked
        provider.WaitForAssertion(() =>
        {
            var generateButton = provider.FindAll("button")
                .FirstOrDefault(b => b.TextContent.Contains("Generate"));
            Assert.That(generateButton, Is.Not.Null);
            Assert.That(generateButton!.GetAttribute("disabled"), Is.Not.Null);
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
