using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BanCelebrationSettings tests.
/// Registers mocked repositories and services in the constructor.
/// </summary>
public class BanCelebrationSettingsTestContext : BunitContext
{
    protected IBanCelebrationGifRepository GifRepository { get; }
    protected IBanCelebrationCaptionRepository CaptionRepository { get; }
    protected IThumbnailService ThumbnailService { get; }
    protected IConfigService ConfigService { get; }

    protected BanCelebrationSettingsTestContext()
    {
        // Create mocks
        GifRepository = Substitute.For<IBanCelebrationGifRepository>();
        CaptionRepository = Substitute.For<IBanCelebrationCaptionRepository>();
        ThumbnailService = Substitute.For<IThumbnailService>();
        ConfigService = Substitute.For<IConfigService>();

        // Configure default behavior - return empty lists
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        GifRepository.GetFullPath(Arg.Any<string>()).Returns(info => $"/data/media/{info.Arg<string>()}");
        ThumbnailService.GenerateThumbnailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>()).Returns(true);
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(BanCelebrationConfig.Default);

        // Register mocks
        Services.AddSingleton(GifRepository);
        Services.AddSingleton(CaptionRepository);
        Services.AddSingleton(ThumbnailService);
        Services.AddSingleton(ConfigService);

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
/// Component tests for BanCelebrationSettings.razor
/// Tests the main settings page for managing GIF library, caption library, and global config.
/// </summary>
[TestFixture]
public class BanCelebrationSettingsTests : BanCelebrationSettingsTestContext
{
    [SetUp]
    public void Setup()
    {
        GifRepository.ClearReceivedCalls();
        CaptionRepository.ClearReceivedCalls();
        ThumbnailService.ClearReceivedCalls();
        ConfigService.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Renders the BanCelebrationSettings component.
    /// </summary>
    private IRenderedComponent<BanCelebrationSettings> RenderComponent()
    {
        return Render<BanCelebrationSettings>();
    }

    /// <summary>
    /// Creates a list of test GIFs.
    /// </summary>
    private static List<BanCelebrationGif> CreateTestGifs()
    {
        return
        [
            new()
            {
                Id = 1,
                FilePath = "ban-gifs/1.gif",
                ThumbnailPath = "ban-gifs/1_thumb.png",
                Name = "Celebration 1",
                FileId = null,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new()
            {
                Id = 2,
                FilePath = "ban-gifs/2.gif",
                ThumbnailPath = "ban-gifs/2_thumb.png",
                Name = "Celebration 2",
                FileId = "cached_file_id",
                CreatedAt = DateTimeOffset.UtcNow
            }
        ];
    }

    /// <summary>
    /// Creates a list of test captions.
    /// </summary>
    private static List<BanCelebrationCaption> CreateTestCaptions()
    {
        return
        [
            new()
            {
                Id = 1,
                Text = "💀 **FATALITY!** {username} has been finished!",
                DmText = "You have been finished!",
                Name = "Mortal Kombat - Fatality",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            },
            new()
            {
                Id = 2,
                Text = "🔨 **BAN HAMMER!** {username} eliminated!",
                DmText = "You have been eliminated!",
                Name = "Ban Hammer",
                CreatedAt = DateTimeOffset.UtcNow
            }
        ];
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasGlobalConfigSection()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Global Configuration"));
    }

    [Test]
    public void HasGifLibrarySection()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("GIF Library"));
    }

    [Test]
    public void HasCaptionLibrarySection()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Caption Library"));
    }

    [Test]
    public void HasTestPreviewSection()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Test Preview"));
    }

    [Test]
    public void HasInfoAlert()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Per-chat configuration"));
        Assert.That(cut.Markup, Does.Contain("Chat Management"));
    }

    #endregion

    #region Global Config Section Tests

    [Test]
    public void GlobalConfigSection_HasSaveButton()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Save Defaults"));
    }

    [Test]
    public void GlobalConfigSection_HasDescription()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Default settings for all chats"));
        Assert.That(cut.Markup, Does.Contain("Individual chats can override"));
    }

    [Test]
    public void GlobalConfigSection_ContainsBanCelebrationChatSettings()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert - Should contain the nested component
        Assert.That(cut.Markup, Does.Contain("Enable Ban Celebrations"));
    }

    #endregion

    #region GIF Library Section Tests

    [Test]
    public void GifLibrarySection_HasAddButton()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Add GIF"));
    }

    [Test]
    public void GifLibrarySection_HasDescription()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Celebratory GIFs"));
        Assert.That(cut.Markup, Does.Contain("Random selection"));
    }

    [Test]
    public void GifLibrarySection_HasTable()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-table"));
    }

    [Test]
    public void GifLibrarySection_HasTableHeaders()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Preview"));
        Assert.That(cut.Markup, Does.Contain("Name"));
        Assert.That(cut.Markup, Does.Contain("Cached"));
        Assert.That(cut.Markup, Does.Contain("Added"));
        Assert.That(cut.Markup, Does.Contain("Actions"));
    }

    [Test]
    public void GifLibrarySection_ShowsEmptyState_WhenNoGifs()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("No GIFs in library"));
    }

    [Test]
    public async Task GifLibrarySection_DisplaysGifs_WhenAvailable()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestGifs());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Celebration 1"));
        Assert.That(cut.Markup, Does.Contain("Celebration 2"));
    }

    [Test]
    public async Task GifLibrarySection_ShowsCachedStatus()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestGifs());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert - Should show Yes/No for cached status
        Assert.That(cut.Markup, Does.Contain("Yes"));
        Assert.That(cut.Markup, Does.Contain("No"));
    }

    [Test]
    public async Task GifLibrarySection_HasDeleteButton()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestGifs());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert - Should have delete icon button
        Assert.That(cut.Markup, Does.Contain("Delete"));
    }

    #endregion

    #region Caption Library Section Tests

    [Test]
    public void CaptionLibrarySection_HasAddButton()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Add Caption"));
    }

    [Test]
    public void CaptionLibrarySection_HasDescription()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Caption templates"));
        Assert.That(cut.Markup, Does.Contain("{username}"));
        Assert.That(cut.Markup, Does.Contain("{chatname}"));
        Assert.That(cut.Markup, Does.Contain("{bancount}"));
    }

    [Test]
    public void CaptionLibrarySection_HasTable()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert - Should have at least 2 tables (GIF and Caption)
        Assert.That(cut.FindAll(".mud-table").Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void CaptionLibrarySection_HasTableHeaders()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Chat Caption"));
        Assert.That(cut.Markup, Does.Contain("DM Caption"));
    }

    [Test]
    public void CaptionLibrarySection_ShowsEmptyState_WhenNoCaptions()
    {
        // Arrange
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("No captions in library"));
    }

    [Test]
    public async Task CaptionLibrarySection_DisplaysCaptions_WhenAvailable()
    {
        // Arrange
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestCaptions());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Mortal Kombat - Fatality"));
        Assert.That(cut.Markup, Does.Contain("Ban Hammer"));
    }

    [Test]
    public async Task CaptionLibrarySection_HasEditAndDeleteButtons()
    {
        // Arrange
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestCaptions());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert - Should have edit and delete buttons
        Assert.That(cut.Markup, Does.Contain("Edit"));
        Assert.That(cut.Markup, Does.Contain("Delete"));
    }

    #endregion

    #region Test Preview Section Tests

    [Test]
    public void TestPreviewSection_HasTestButton()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Test Random Combo"));
    }

    [Test]
    public void TestPreviewSection_HasDescription()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Preview a random GIF + caption combination"));
    }

    [Test]
    public void TestPreviewSection_ButtonDisabled_WhenNoGifsOrCaptions()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var cut = RenderComponent();

        // Assert
        var testButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Test Random Combo"));
        Assert.That(testButton, Is.Not.Null);
        Assert.That(testButton!.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public async Task TestPreviewSection_ButtonEnabled_WhenBothLibrariesHaveContent()
    {
        // Arrange
        GifRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestGifs());
        CaptionRepository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(CreateTestCaptions());

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        var testButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Test Random Combo"));
        Assert.That(testButton, Is.Not.Null);
        Assert.That(testButton!.HasAttribute("disabled"), Is.False);
    }

    #endregion

    #region Data Loading Tests

    [Test]
    public async Task LoadsGifs_OnInitialize()
    {
        // Act
        _ = RenderComponent();
        await Task.Delay(50);

        // Assert
        await GifRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadsCaptions_OnInitialize()
    {
        // Act
        _ = RenderComponent();
        await Task.Delay(50);

        // Assert
        await CaptionRepository.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadsGlobalConfig_OnInitialize()
    {
        // Act
        _ = RenderComponent();
        await Task.Delay(50);

        // Assert - Should load config for chat_id=0 (global)
        await ConfigService.Received(1).GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            0);
    }

    #endregion

    #region Paper Styling Tests

    [Test]
    public void AllSections_HavePaperContainers()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert - Should have multiple paper containers
        Assert.That(cut.FindAll(".mud-paper").Count, Is.GreaterThanOrEqualTo(4));
    }

    #endregion
}
