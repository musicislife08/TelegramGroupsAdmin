using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for BotGeneralSettings tests.
/// Registers mocked IConfigService, IConfigRepository, and IMessageHistoryService.
/// </summary>
public class BotGeneralSettingsTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }
    protected IConfigRepository ConfigRepository { get; }
    protected IMessageHistoryService MessageHistoryService { get; }

    protected BotGeneralSettingsTestContext()
    {
        // Create mocks
        ConfigService = Substitute.For<IConfigService>();
        ConfigRepository = Substitute.For<IConfigRepository>();
        MessageHistoryService = Substitute.For<IMessageHistoryService>();

        // Default config returns
        ConfigService.GetAsync<TelegramBotConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(TelegramBotConfig.Default);
        ConfigService.GetEffectiveAsync<BotProtectionConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(BotProtectionConfig.Default);
        ConfigService.GetTelegramBotTokenAsync().Returns((string?)null);

        // Register mocks
        Services.AddSingleton(ConfigService);
        Services.AddSingleton(ConfigRepository);
        Services.AddSingleton(MessageHistoryService);

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
/// Component tests for BotGeneralSettings.razor
/// Tests the bot service control and protection settings component.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests recommended for:
/// - Testing form submission and validation flows
/// - Testing real-time config change notifications
/// - Testing bot token visibility toggle interaction
/// - Testing whitelist parsing on save
/// </remarks>
[TestFixture]
public class BotGeneralSettingsTests : BotGeneralSettingsTestContext
{
    [SetUp]
    public void Setup()
    {
        ConfigService.ClearReceivedCalls();
        ConfigRepository.ClearReceivedCalls();
    }

    #region Structure Tests

    [Test]
    public void RendersWithoutError()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void DisplaysBotServiceControlSection()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Bot Service Control"));
        });
    }

    [Test]
    public void DisplaysBotProtectionSection()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Bot Protection Settings"));
        });
    }

    [Test]
    public void DisplaysCachedInviteLinksSection()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Cached Invite Links"));
        });
    }

    #endregion

    #region Bot Token Tests

    [Test]
    public void ShowsBotTokenNotSetChip_WhenNoToken()
    {
        // Arrange
        ConfigService.GetTelegramBotTokenAsync().Returns((string?)null);

        // Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Bot Token Not Set"));
        });
    }

    [Test]
    public void ShowsBotTokenConfiguredChip_WhenTokenExists()
    {
        // Arrange
        ConfigService.GetTelegramBotTokenAsync().Returns("1234567890:ABCdefGHI");

        // Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Bot Token Configured"));
        });
    }

    [Test]
    public void HasBotTokenField()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Bot Token"));
        });
    }

    [Test]
    public void HasBotTokenHelperText()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Get your bot token from BotFather"));
        });
    }

    #endregion

    #region Bot Enable Switch Tests

    [Test]
    public void HasEnableBotServiceSwitch()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Enable Telegram Bot Service"));
        });
    }

    [Test]
    public void HasMasterSwitchDescription()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Master switch"));
        });
    }

    #endregion

    #region Bot Protection Settings Tests

    [Test]
    public void HasEnableBotProtectionSwitch()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Enable Bot Protection"));
        });
    }

    [Test]
    public void HasAutoBanBotsSwitch()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Auto-Ban Unauthorized Bots"));
        });
    }

    [Test]
    public void HasAllowAdminInvitedBotsSwitch()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Allow Admin-Invited Bots"));
        });
    }

    [Test]
    public void HasLogBotEventsSwitch()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Log Bot Events"));
        });
    }

    [Test]
    public void HasWhitelistedBotsField()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Whitelisted Bots"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void HasSaveBotServiceButton()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Save Bot Service Settings"));
        });
    }

    [Test]
    public void HasResetButton()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Reset"));
        });
    }

    [Test]
    public void HasSaveConfigurationButton()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Save Configuration"));
        });
    }

    [Test]
    public void HasResetToDefaultsButton()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Reset to Defaults"));
        });
    }

    [Test]
    public void HasClearCachedLinksButton()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Clear All Cached Links"));
        });
    }

    #endregion

    #region Info Alert Tests

    [Test]
    public void DisplaysImmediateChangeNote()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Changes take effect immediately"));
        });
    }

    [Test]
    public void DisplaysHowBotProtectionWorks()
    {
        // Arrange & Act
        var cut = Render<BotGeneralSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("How Bot Protection Works"));
        });
    }

    #endregion
}
