using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using System.Security.Claims;
using TelegramGroupsAdmin.BackgroundJobs.Constants;
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
    protected IBackupRotationService RotationService { get; }
    protected AuthenticationStateProvider AuthStateProvider { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected BackupPassphraseRotationDialogTestContext()
    {
        // Create mocks
        PassphraseService = Substitute.For<IPassphraseManagementService>();
        RotationService = Substitute.For<IBackupRotationService>();
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
        Services.AddSingleton(RotationService);
        this.AddTestWebUser();
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
        RotationService.ClearReceivedCalls();
        // The fixture instance (and its substitutes) is shared by every test, so reset the default
        // here: a clean backup directory, which opens the dialog on the confirmation step.
        RotationService.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRotationScan(1, [], []));
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

    #region Custom Passphrase Length Tests

    [Test]
    public async Task CustomPassphrase_ShorterThanTheRecommendedMinimum_ShowsWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Use custom passphrase")));

        // Act - switch to a custom passphrase one character short of the minimum
        await EnterCustomPassphraseAsync(provider, new string('a', EncryptionConstants.MinimumPassphraseLengthChars - 1));

        // Assert - the helper text and the warning both state the constant's value
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain($"Minimum {EncryptionConstants.MinimumPassphraseLengthChars} characters recommended"));
            Assert.That(provider.Markup, Does.Contain($"Recommend at least {EncryptionConstants.MinimumPassphraseLengthChars} characters"));
        });
    }

    [Test]
    public async Task CustomPassphrase_AtTheRecommendedMinimum_ShowsNoWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Use custom passphrase")));

        // Act - a custom passphrase of exactly the minimum length
        await EnterCustomPassphraseAsync(provider, new string('a', EncryptionConstants.MinimumPassphraseLengthChars));

        // Assert - the custom field rendered, and the warning did not
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain($"Minimum {EncryptionConstants.MinimumPassphraseLengthChars} characters recommended"));
            Assert.That(provider.Markup, Does.Not.Contain("Passphrase is too short"));
        });
    }

    private static async Task EnterCustomPassphraseAsync(IRenderedComponent<MudDialogProvider> provider, string passphrase)
    {
        var customSwitch = provider.FindComponent<MudSwitch<bool>>();
        await provider.InvokeAsync(() => customSwitch.Instance.ValueChanged.InvokeAsync(true));

        var passphraseField = provider.FindComponents<MudTextField<string>>()
            .Single(f => f.Instance.Label == "Custom Passphrase");
        await provider.InvokeAsync(() => passphraseField.Instance.ValueChanged.InvokeAsync(passphrase));
    }

    #endregion

    #region Damaged Backup Pre-check Tests

    private const string RepairFieldLabel = "Original passphrase";

    private void ScanReturns(string[] wrapped, string[] unreadable) =>
        RotationService.ScanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BackupRotationScan(1, wrapped, unreadable));

    private static IElement Button(IRenderedComponent<MudDialogProvider> provider, string text) =>
        provider.FindAll("button").First(b => b.TextContent.Contains(text));

    private static Task CheckAsync(IRenderedComponent<MudDialogProvider> provider, string labelText) =>
        provider.InvokeAsync(() => provider.FindComponents<MudCheckBox<bool>>()
            .Single(c => c.Markup.Contains(labelText)).Instance.ValueChanged.InvokeAsync(true));

    private static Task EnterRepairPassphraseAsync(IRenderedComponent<MudDialogProvider> provider, string passphrase) =>
        provider.InvokeAsync(() => provider.FindComponents<MudTextField<string>>()
            .Single(f => f.Instance.Label == RepairFieldLabel).Instance.ValueChanged.InvokeAsync(passphrase));

    [Test]
    public void CleanScan_ShowsConfirmationStep()
    {
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("What will happen"));
            Assert.That(provider.Markup, Does.Not.Contain(RepairFieldLabel));
        });
        RotationService.Received().ScanAsync("/data/backups", Arg.Any<CancellationToken>());
    }

    [Test]
    public void WrappedFiles_ShowRepairStep_WithCountAndPassphraseField()
    {
        ScanReturns(["a.tar.gz", "b.tar.gz"], []);
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("2 backups"));
            Assert.That(provider.Markup, Does.Contain(RepairFieldLabel));
            Assert.That(provider.Markup, Does.Not.Contain("What will happen"));
        });
    }

    [Test]
    public async Task RepairAttempt_CallsServiceWithEnteredPassphrase_AndShowsRemaining()
    {
        ScanReturns(["a.tar.gz", "c.tar.gz"], []);
        RotationService.RepairWrappedAsync("/data/backups", "my-setup-passphrase", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WrappedRepairResult(1, ["c.tar.gz"]));
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain(RepairFieldLabel)));

        await EnterRepairPassphraseAsync(provider, "my-setup-passphrase");
        await provider.InvokeAsync(() => Button(provider, "Repair").Click());

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Repaired 1"));
            Assert.That(provider.Markup, Does.Contain("c.tar.gz"));
            Assert.That(provider.Markup, Does.Contain(RepairFieldLabel), "the user can try another passphrase");
        });
        await RotationService.Received(1).RepairWrappedAsync("/data/backups", "my-setup-passphrase",
            WebUserRenderHelper.TestWebUser.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AllRepaired_MovesToConfirmation()
    {
        ScanReturns(["a.tar.gz"], []);
        RotationService.RepairWrappedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new WrappedRepairResult(1, []));
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain(RepairFieldLabel)));

        await EnterRepairPassphraseAsync(provider, "my-setup-passphrase");
        await provider.InvokeAsync(() => Button(provider, "Repair").Click());

        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("What will happen")));
    }

    [Test]
    public async Task DeleteDamaged_RequiresConfirmation_ThenCallsService()
    {
        ScanReturns(["c.tar.gz"], []);
        RotationService.DeleteDamagedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(1);
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Delete these 1 backups")));

        Assert.That(Button(provider, "Delete these 1 backups").GetAttribute("disabled"), Is.Not.Null,
            "delete stays disabled until the user confirms");

        await CheckAsync(provider, "permanently deleted");
        await provider.InvokeAsync(() => Button(provider, "Delete these 1 backups").Click());

        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("What will happen")));
        await RotationService.Received(1).DeleteDamagedAsync("/data/backups",
            Arg.Is<IReadOnlyCollection<string>>(names => names!.SequenceEqual(new[] { "c.tar.gz" })),
            WebUserRenderHelper.TestWebUser.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ContinueWithoutThem_MovesToConfirmation_WithoutDeleting()
    {
        ScanReturns(["c.tar.gz"], []);
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Continue without them")));

        await provider.InvokeAsync(() => Button(provider, "Continue without them").Click());

        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("What will happen")));
        await RotationService.DidNotReceive().DeleteDamagedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void UnreadableFiles_AreListedForDeletion()
    {
        ScanReturns([], ["junk.tar.gz"]);
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("junk.tar.gz"));
            Assert.That(provider.Markup, Does.Contain("Delete these 1 backups"));
            Assert.That(provider.Markup, Does.Not.Contain(RepairFieldLabel), "nothing to repair, only unreadable files");
        });
    }

    #endregion

    #region Rotation Passphrase Tests

    [Test]
    public async Task Complete_PassesTheShownPassphraseToRotation()
    {
        string? rotatedTo = null;
        await PassphraseService.RotatePassphraseAsync(Arg.Do<string>(p => rotatedTo = p), Arg.Any<string>(), Arg.Any<string>());
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("What will happen")));

        await CheckAsync(provider, "re-encrypt all existing backups");
        await provider.InvokeAsync(() => Button(provider, "Generate New Passphrase").Click());
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Your NEW Backup Encryption Passphrase")));
        var shownMarkup = provider.Markup;

        await CheckAsync(provider, "securely saved this NEW passphrase");
        await provider.InvokeAsync(() => Button(provider, "Start Rotation").Click());

        provider.WaitForAssertion(() => Assert.That(rotatedTo, Is.Not.Null));
        Assert.That(shownMarkup, Does.Contain(rotatedTo!), "the passphrase rotated to is the one the user was shown");
    }

    [Test]
    public async Task CustomPassphrase_IsTheOneRotatedTo()
    {
        const string custom = "my-own-custom-passphrase-123";
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Use custom passphrase")));

        await EnterCustomPassphraseAsync(provider, custom);
        await CheckAsync(provider, "re-encrypt all existing backups");
        await provider.InvokeAsync(() => Button(provider, "Set Custom Passphrase").Click());
        provider.WaitForAssertion(() => Assert.That(provider.Markup, Does.Contain("Your NEW Backup Encryption Passphrase")));
        await CheckAsync(provider, "securely saved this NEW passphrase");
        await provider.InvokeAsync(() => Button(provider, "Start Rotation").Click());

        await PassphraseService.Received(1).RotatePassphraseAsync(custom, "/data/backups", WebUserRenderHelper.TestWebUser.Id);
    }

    #endregion
}
