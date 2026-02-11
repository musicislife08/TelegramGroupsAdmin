using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BanCelebrationChatSettings tests.
/// Registers mocked services in the constructor.
/// </summary>
public class BanCelebrationChatSettingsTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }

    protected BanCelebrationChatSettingsTestContext()
    {
        // Create mocks
        ConfigService = Substitute.For<IConfigService>();

        // Configure default behavior - return default config
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(BanCelebrationConfig.Default);

        // Register mocks
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
/// Component tests for BanCelebrationChatSettings.razor
/// Tests the per-chat configuration component for ban celebrations.
/// </summary>
[TestFixture]
public class BanCelebrationChatSettingsTests : BanCelebrationChatSettingsTestContext
{
    [SetUp]
    public void Setup()
    {
        ConfigService.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Renders the BanCelebrationChatSettings component with specified parameters.
    /// </summary>
    private IRenderedComponent<BanCelebrationChatSettings> RenderComponent(
        long chatId = -100123456789,
        bool showLibraryHint = true)
    {
        return Render<BanCelebrationChatSettings>(parameters => parameters
            .Add(p => p.Chat, ChatIdentity.FromId(chatId))
            .Add(p => p.ShowLibraryHint, showLibraryHint));
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasPaperContainer()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("mud-paper"));
    }

    [Test]
    public void HasTitle()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Ban Celebration"));
    }

    [Test]
    public void HasDescription()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Post celebratory GIFs when spammers are banned"));
    }

    #endregion

    #region Enable Toggle Tests

    [Test]
    public void HasEnableToggle()
    {
        // Arrange & Act
        var cut = RenderComponent();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Enable Ban Celebrations"));
        Assert.That(cut.Markup, Does.Contain("mud-switch"));
    }

    [Test]
    public async Task EnableToggle_DefaultsToDisabled()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = false });

        // Act
        var cut = RenderComponent();

        // Wait for async load
        await Task.Delay(50);
        cut.Render();

        // Assert - Trigger options should not be visible when disabled
        Assert.That(cut.Markup, Does.Not.Contain("Trigger on auto-ban"));
    }

    [Test]
    public async Task EnableToggle_ShowsTriggersWhenEnabled()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();

        // Wait for async load
        await Task.Delay(50);
        cut.Render();

        // Assert - Trigger options should be visible when enabled
        Assert.That(cut.Markup, Does.Contain("Trigger on auto-ban"));
        Assert.That(cut.Markup, Does.Contain("Trigger on manual ban"));
    }

    #endregion

    #region Trigger Options Tests

    [Test]
    public async Task HasAutoBanTriggerCheckbox()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Trigger on auto-ban (spam detection)"));
    }

    [Test]
    public async Task HasManualBanTriggerCheckbox()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Trigger on manual ban (admin /ban command)"));
    }

    #endregion

    #region DM Toggle Tests

    [Test]
    public async Task HasSendToBannedUserToggle()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Send celebration to banned user via DM"));
    }

    [Test]
    public async Task DmToggle_HasHelperText()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Maximum impact"));
        Assert.That(cut.Markup, Does.Contain("DM-based welcome mode"));
    }

    #endregion

    #region Library Hint Tests

    [Test]
    public void ShowsLibraryHint_WhenEnabled()
    {
        // Arrange & Act
        var cut = RenderComponent(showLibraryHint: true);

        // Assert
        Assert.That(cut.Markup, Does.Contain("Settings"));
        Assert.That(cut.Markup, Does.Contain("Moderation"));
        Assert.That(cut.Markup, Does.Contain("Ban Celebration"));
    }

    [Test]
    public void HidesLibraryHint_WhenDisabled()
    {
        // Arrange & Act
        var cut = RenderComponent(showLibraryHint: false);

        // Assert - Should not contain the alert about global library
        Assert.That(cut.Markup, Does.Not.Contain("global library"));
    }

    [Test]
    public void LibraryHint_HasInfoSeverity()
    {
        // Arrange & Act
        var cut = RenderComponent(showLibraryHint: true);

        // Assert - MudBlazor renders info alerts with mud-alert-text-info class
        Assert.That(cut.Markup, Does.Contain("mud-alert-text-info"));
    }

    #endregion

    #region Config Loading Tests

    [Test]
    public async Task LoadsConfigForSpecifiedChatId()
    {
        // Arrange
        const long testChatId = -100999888777;

        // Act
        _ = RenderComponent(chatId: testChatId);
        await Task.Delay(50);

        // Assert
        await ConfigService.Received(1).GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            testChatId);
    }

    [Test]
    public async Task HandlesConfigLoadError_Gracefully()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns<ValueTask<BanCelebrationConfig?>>(x => throw new Exception("Config load failed"));

        // Act - Should not throw
        var cut = RenderComponent();
        await Task.Delay(50);

        // Assert - Component should still render
        Assert.That(cut.Markup, Does.Contain("Ban Celebration"));
    }

    #endregion

    #region Divider Tests

    [Test]
    public async Task HasDividers_WhenEnabled()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert - Should have dividers separating sections
        Assert.That(cut.Markup, Does.Contain("mud-divider"));
    }

    #endregion

    #region Section Headers Tests

    [Test]
    public async Task HasTriggersHeader_WhenEnabled()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("Triggers"));
    }

    [Test]
    public async Task HasDmHeader_WhenEnabled()
    {
        // Arrange
        ConfigService.GetAsync<BanCelebrationConfig>(
            ConfigType.BanCelebration,
            Arg.Any<long>()).Returns(new BanCelebrationConfig { Enabled = true });

        // Act
        var cut = RenderComponent();
        await Task.Delay(50);
        cut.Render();

        // Assert
        Assert.That(cut.Markup, Does.Contain("DM to Banned User"));
    }

    #endregion
}
