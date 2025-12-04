using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using System.Security.Claims;
using TelegramGroupsAdmin.BackgroundJobs.Services.Backup;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BackupPassphraseRotationDialog tests.
/// Registers mocked IPassphraseManagementService and AuthenticationStateProvider.
/// </summary>
public class BackupPassphraseRotationDialogTestContext : BunitContext
{
    protected IPassphraseManagementService PassphraseService { get; }
    protected AuthenticationStateProvider AuthStateProvider { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected BackupPassphraseRotationDialogTestContext()
    {
        // Create mocks
        PassphraseService = Substitute.For<IPassphraseManagementService>();
        AuthStateProvider = Substitute.For<AuthenticationStateProvider>();

        // Setup default auth state with authenticated user
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            new(ClaimTypes.Name, "testuser@example.com")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var authState = new AuthenticationState(principal);
        AuthStateProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));

        // Register mocks
        Services.AddSingleton(PassphraseService);
        Services.AddSingleton(AuthStateProvider);

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
/// Component tests for BackupPassphraseRotationDialog.razor
/// Tests the dialog for rotating backup encryption passphrase.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests recommended for:
/// - Testing the full rotation flow (Confirmation → Processing → DisplayNewPassphrase)
/// - Testing passphrase copy functionality
/// - Testing background job queuing after confirmation
/// - Testing custom passphrase validation
/// </remarks>
[TestFixture]
public class BackupPassphraseRotationDialogTests : BackupPassphraseRotationDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        PassphraseService.ClearReceivedCalls();
    }

    #region Helper Methods

    private async Task<IDialogReference> OpenDialogAsync(string backupDirectory = "/data/backups")
    {
        var parameters = new DialogParameters<BackupPassphraseRotationDialog>
        {
            { x => x.BackupDirectory, backupDirectory }
        };
        return await DialogService.ShowAsync<BackupPassphraseRotationDialog>("Rotate Backup Passphrase", parameters);
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
            Assert.That(provider.Markup, Does.Contain("Rotate Backup Passphrase"));
        });
    }

    #endregion

    #region Confirmation Step Tests

    [Test]
    public void DisplaysWhatWillHappenWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("What will happen"));
        });
    }

    [Test]
    public void DisplaysReEncryptionInfo()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("re-encrypted"));
        });
    }

    [Test]
    public void DisplaysBackupDirectory()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(backupDirectory: "/custom/backups");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("/custom/backups"));
        });
    }

    [Test]
    public void DisplaysWhyRotateInfo()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Why rotate?"));
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
            Assert.That(provider.Markup, Does.Contain("I understand this will re-encrypt"));
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
    public void HasGenerateNewPassphraseButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Generate New Passphrase"));
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
                .FirstOrDefault(b => b.TextContent.Contains("Generate New"));
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
