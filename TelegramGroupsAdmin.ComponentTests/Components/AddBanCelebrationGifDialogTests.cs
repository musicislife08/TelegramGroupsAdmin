using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for AddBanCelebrationGifDialog tests.
/// Registers mocked repositories and services.
/// </summary>
public class AddBanCelebrationGifDialogTestContext : BunitContext
{
    protected IBanCelebrationGifRepository GifRepository { get; }
    protected IThumbnailService ThumbnailService { get; }
    protected IPhotoHashService PhotoHashService { get; }
    protected IDialogService DialogService { get; private set; } = null!;
    protected ISnackbar Snackbar { get; }

    protected AddBanCelebrationGifDialogTestContext()
    {
        // Create mocks
        GifRepository = Substitute.For<IBanCelebrationGifRepository>();
        ThumbnailService = Substitute.For<IThumbnailService>();
        PhotoHashService = Substitute.For<IPhotoHashService>();
        Snackbar = Substitute.For<ISnackbar>();

        // Configure default behavior
        GifRepository.GetFullPath(Arg.Any<string>()).Returns(info => $"/data/media/{info.Arg<string>()}");
        ThumbnailService.GenerateThumbnailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()).Returns(true);

        // Configure photo hash service - returns a dummy hash by default
        PhotoHashService.ComputePhotoHashAsync(Arg.Any<string>())
            .Returns(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });

        // Register mocks
        Services.AddSingleton(GifRepository);
        Services.AddSingleton(ThumbnailService);
        Services.AddSingleton(PhotoHashService);
        Services.AddSingleton(Snackbar);

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

    /// <summary>
    /// Renders a wrapper component that includes MudDialogProvider.
    /// Must be called before opening dialogs.
    /// </summary>
    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for AddBanCelebrationGifDialog.razor
/// Tests the dialog for adding GIFs via file upload or URL.
/// </summary>
[TestFixture]
public class AddBanCelebrationGifDialogTests : AddBanCelebrationGifDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        GifRepository.ClearReceivedCalls();
        ThumbnailService.ClearReceivedCalls();
        PhotoHashService.ClearReceivedCalls();
        Snackbar.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Opens the AddBanCelebrationGifDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync()
    {
        return await DialogService.ShowAsync<AddBanCelebrationGifDialog>("Add GIF");
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
    public void HasTitleWithIcon()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add Ban Celebration GIF"));
            Assert.That(provider.Markup, Does.Contain("mud-icon"));
        });
    }

    [Test]
    public void HasTabs()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-tabs"));
            Assert.That(provider.Markup, Does.Contain("Upload File"));
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });
    }

    #endregion

    #region Upload Tab Tests

    [Test]
    public void UploadTab_HasNameField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name (optional)"));
        });
    }

    [Test]
    public void UploadTab_HasFileUploadButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Select GIF or MP4 file"));
        });
    }

    [Test]
    public void UploadTab_AcceptsGifAndMp4()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain(".gif,.mp4"));
        });
    }

    #endregion

    #region URL Tab Tests

    [Test]
    public void UrlTab_HasUrlField()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render and click URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GIF URL"));
        });
    }

    [Test]
    public void UrlTab_HasPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render and click URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("https://example.com/animation.gif"));
        });
    }

    [Test]
    public void UrlTab_HasHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render and click URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Direct link to a GIF or MP4 file"));
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
    public void HasAddGifButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add GIF"));
        });
    }

    [Test]
    public void AddGifButton_DisabledByDefault()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Add GIF button should be disabled when no file selected
        provider.WaitForAssertion(() =>
        {
            var addButton = provider.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Add GIF"));
            Assert.That(addButton, Is.Not.Null);
            Assert.That(addButton!.HasAttribute("disabled"), Is.True);
        });
    }

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
        var cancelButton = provider.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        // Assert - Dialog content should be removed
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion

    #region Helper Text Tests

    [Test]
    public void HasNameHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("A friendly name for this GIF"));
        });
    }

    #endregion

    #region Duplicate Detection & Cleanup Tests

    /// <summary>
    /// Configures mocks to simulate a duplicate being detected during upload.
    /// Returns the pending GIF that should be deleted on cancel.
    /// </summary>
    private BanCelebrationGif ConfigureDuplicateScenario()
    {
        var pendingGif = new BanCelebrationGif { Id = 99, Name = "Pending", FilePath = "ban-gifs/pending.gif" };
        var existingGif = new BanCelebrationGif { Id = 1, Name = "Existing", FilePath = "ban-gifs/existing.gif" };

        // When AddFromUrlAsync is called, return the pending GIF
        GifRepository.AddFromUrlAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(pendingGif);

        // When FindSimilarAsync is called, return the existing GIF (simulating duplicate found)
        GifRepository.FindSimilarAsync(Arg.Any<byte[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(existingGif);

        return pendingGif;
    }

    [Test]
    public async Task DuplicateWarning_CancelUploadButton_DeletesPendingGif()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var pendingGif = ConfigureDuplicateScenario();

        var dialogRef = await OpenDialogAsync();

        // Wait for dialog and switch to URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Enter a URL
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GIF URL"));
        });

        var urlInput = provider.Find("input[placeholder='https://example.com/animation.gif']");
        urlInput.Change("https://example.com/test.gif");

        // Click Add GIF button to trigger submission
        var addButton = provider.FindAll("button").First(b => b.TextContent.Contains("Add GIF"));
        addButton.Click();

        // Wait for duplicate warning to appear
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Similar GIF already exists"));
        });

        // Act - Click "Cancel Upload" button in warning
        var cancelUploadButton = provider.FindAll("button").First(b => b.TextContent.Contains("Cancel Upload"));
        cancelUploadButton.Click();

        // Assert - DeleteAsync should have been called with the pending GIF's ID
        await GifRepository.Received(1).DeleteAsync(pendingGif.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DuplicateWarning_MainCancelButton_DeletesPendingGif()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var pendingGif = ConfigureDuplicateScenario();

        var dialogRef = await OpenDialogAsync();

        // Wait for dialog and switch to URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Enter a URL
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GIF URL"));
        });

        var urlInput = provider.Find("input[placeholder='https://example.com/animation.gif']");
        urlInput.Change("https://example.com/test.gif");

        // Click Add GIF button to trigger submission
        var addButton = provider.FindAll("button").First(b => b.TextContent.Contains("Add GIF"));
        addButton.Click();

        // Wait for duplicate warning to appear
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Similar GIF already exists"));
        });

        // Act - Click the main "Cancel" button (not "Cancel Upload")
        // The main Cancel button is in the dialog actions, not in the warning
        var cancelButtons = provider.FindAll("button").Where(b => b.TextContent.Trim() == "Cancel").ToList();
        var mainCancelButton = cancelButtons.First(); // The main cancel button in dialog actions
        mainCancelButton.Click();

        // Assert - DeleteAsync should have been called with the pending GIF's ID
        await GifRepository.Received(1).DeleteAsync(pendingGif.Id, Arg.Any<CancellationToken>());
    }

    // NOTE: Testing Escape key / backdrop click cleanup is better suited for E2E Playwright tests
    // since those are browser-level interactions that bUnit cannot properly simulate.
    // The IAsyncDisposable implementation provides a safety net for those scenarios.

    [Test]
    public async Task DuplicateWarning_KeepBothButton_DoesNotDeleteGif()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var pendingGif = ConfigureDuplicateScenario();

        var dialogRef = await OpenDialogAsync();

        // Wait for dialog and switch to URL tab
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("From URL"));
        });

        var urlTab = provider.FindAll(".mud-tab").First(t => t.TextContent.Contains("From URL"));
        urlTab.Click();

        // Enter a URL
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GIF URL"));
        });

        var urlInput = provider.Find("input[placeholder='https://example.com/animation.gif']");
        urlInput.Change("https://example.com/test.gif");

        // Click Add GIF button to trigger submission
        var addButton = provider.FindAll("button").First(b => b.TextContent.Contains("Add GIF"));
        addButton.Click();

        // Wait for duplicate warning to appear
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Similar GIF already exists"));
        });

        // Clear any previous calls before clicking Keep Both
        GifRepository.ClearReceivedCalls();

        // Act - Click "Keep Both" to confirm keeping the duplicate
        var keepBothButton = provider.FindAll("button").First(b => b.TextContent.Contains("Keep Both"));
        keepBothButton.Click();

        // Wait for dialog to close
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Similar GIF already exists"));
        });

        // Assert - UpdatePhotoHashAsync SHOULD have been called to save the hash
        await GifRepository.Received(1).UpdatePhotoHashAsync(pendingGif.Id, Arg.Any<byte[]>(), Arg.Any<CancellationToken>());

        // Assert - DeleteAsync should NOT have been called after clicking Keep Both
        await GifRepository.DidNotReceive().DeleteAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
