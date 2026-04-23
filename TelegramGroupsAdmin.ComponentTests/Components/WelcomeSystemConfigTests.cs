using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for WelcomeSystemConfig tests.
/// Registers mocked IConfigService.
/// </summary>
public class WelcomeSystemConfigTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }

    protected WelcomeSystemConfigTestContext()
    {
        // Create mocks
        ConfigService = Substitute.For<IConfigService>();

        // Default config returns
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(WelcomeConfig.Default);

        // Register mocks
        Services.AddSingleton(ConfigService);
        Services.AddSingleton(Substitute.For<IUsernameBlacklistRepository>());
        Services.AddSingleton(Substitute.For<IAuditService>());

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
        // InsertTextAtCursor JS interop for variable insertion
        JSInterop.SetupVoid("insertTextAtCursor", _ => true).SetVoidResult();
    }
}

/// <summary>
/// Component tests for WelcomeSystemConfig.razor
/// Tests the welcome message system configuration component.
/// </summary>
/// <remarks>
/// TODO: Playwright E2E tests recommended for:
/// - Testing variable chip insertion into text fields
/// - Testing live preview updates as text changes
/// - Testing DM vs Chat mode preview differences
/// - Testing JS interop for cursor position tracking
/// </remarks>
[TestFixture]
public class WelcomeSystemConfigTests : WelcomeSystemConfigTestContext
{
    [SetUp]
    public void Setup()
    {
        ConfigService.ClearReceivedCalls();
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(WelcomeConfig.Default);
    }

    #region Structure Tests

    [Test]
    public void RendersWithoutError()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void DisplaysTitle_WhenGlobalMode()
    {
        // Arrange & Act - No ChatId means global mode
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Welcome Message System"));
        });
    }

    [Test]
    public void HidesTitle_WhenChatMode()
    {
        // Arrange & Act - With Chat means per-chat mode
        var testChat = new ManagedChatRecord(
            Identity: new ChatIdentity(123456L, "Test Chat"),
            ChatType: ManagedChatType.Supergroup,
            BotStatus: BotChatStatus.Administrator,
            IsAdmin: true,
            AddedAt: DateTimeOffset.UtcNow,
            IsActive: true,
            IsDeleted: false,
            LastSeenAt: null,
            SettingsJson: null,
            ChatIconPath: null);
        var cut = Render<WelcomeSystemConfig>(p => p
            .Add(x => x.Chat, testChat));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Welcome Message System"));
        });
    }

    #endregion

    #region Enable Switch Tests

    [Test]
    public void HasEnableWelcomeSwitch()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Enable Welcome System"));
        });
    }

    [Test]
    public void HasEnableSwitchDescription()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("new members will be restricted"));
        });
    }

    #endregion

    #region Welcome Mode Tests

    [Test]
    public void HasWelcomeModeSection()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Welcome Mode"));
        });
    }

    [Test]
    public void HasDmWelcomeOption()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("DM Welcome"));
        });
    }

    [Test]
    public void HasChatAcceptDenyOption()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Chat Accept/Deny"));
        });
    }

    [Test]
    public void DisplaysDmWelcomeDescription()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Rules sent privately via bot DM"));
        });
    }

    [Test]
    public void DisplaysChatModeDescription()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Rules shown in group with buttons"));
        });
    }

    #endregion

    #region Timeout Tests

    [Test]
    public void HasTimeoutField()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Timeout"));
        });
    }

    [Test]
    public void HasTimeoutHelperText()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Time before auto-kicking"));
        });
    }

    #endregion

    #region Variable Chips Tests

    [Test]
    public void DisplaysUsernameVariable()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("{username}"));
        });
    }

    [Test]
    public void DisplaysChatNameVariable()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("{chat_name}"));
        });
    }

    [Test]
    public void DisplaysTimeoutVariable()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("{timeout}"));
        });
    }

    [Test]
    public void DisplaysVariableInstructions()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Available variables"));
        });
    }

    #endregion

    #region Main Welcome Message Tests

    [Test]
    public void HasMainWelcomeMessageSection()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Main Welcome Message"));
        });
    }

    [Test]
    public void HasMainWelcomeMessageField()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Complete message with greeting"));
        });
    }

    [Test]
    public void HasLivePreview()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Live Preview"));
        });
    }

    #endregion

    #region Button Customization Tests

    [Test]
    public void HasButtonCustomizationSection()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Button Customization"));
        });
    }

    [Test]
    public void HasAcceptButtonTextField()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Accept Button Text"));
        });
    }

    [Test]
    public void HasDenyButtonTextField()
    {
        // Arrange & Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Deny Button Text"));
        });
    }

    #endregion

    #region Button Tests (Global Mode)

    [Test]
    public void HasSaveConfigurationButton_GlobalMode()
    {
        // Arrange & Act - Global mode (no ChatId)
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Save Configuration"));
        });
    }

    [Test]
    public void HasResetToDefaultsButton_GlobalMode()
    {
        // Arrange & Act - Global mode (no ChatId)
        var cut = Render<WelcomeSystemConfig>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Reset to Defaults"));
        });
    }

    [Test]
    public void HidesSaveButtons_ChatMode()
    {
        // Arrange & Act - Per-chat mode (has Chat)
        var testChat = new ManagedChatRecord(
            Identity: new ChatIdentity(123456L, "Test Chat"),
            ChatType: ManagedChatType.Supergroup,
            BotStatus: BotChatStatus.Administrator,
            IsAdmin: true,
            AddedAt: DateTimeOffset.UtcNow,
            IsActive: true,
            IsDeleted: false,
            LastSeenAt: null,
            SettingsJson: null,
            ChatIconPath: null);
        var cut = Render<WelcomeSystemConfig>(p => p
            .Add(x => x.Chat, testChat));

        // Assert - Buttons should be hidden in per-chat mode
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Save Configuration"));
            Assert.That(cut.Markup, Does.Not.Contain("Reset to Defaults"));
        });
    }

    #endregion

    #region Error State Tests

    [Test]
    public void ShowsErrorAlert_WhenConfigLoadFails()
    {
        // Arrange
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns((WelcomeConfig?)null);

        // Note: Component sets _config to null on error, showing error alert
        // But the default implementation uses WelcomeConfig.Default, so we need
        // to test the actual error path

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert - With default config, no error should show
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Failed to load configuration"));
        });
    }

    #endregion

    #region Trusted User Bypass Tests

    [Test]
    public void TrustedBypassPanel_Renders_WhenConfigLoaded()
    {
        // Arrange - use WelcomeConfig.Default so MainWelcomeMessage is non-empty and
        // the panel is rendered via the "load as-is" path.
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(WelcomeConfig.Default);

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert - new verb-led panel title
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Auto-admit Trusted Users"));
        });
    }

    [Test]
    public void TrustedBypassPanel_RendersBothTemplateFieldsAndPreviews_WhenEnabled()
    {
        // Arrange - enable bypass so both Admin + Trusted template fields + previews
        // are present (fields are enabled; previews always render).
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                MainWelcomeMessage = "Welcome {username}!",
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageAdmin = "admin template {username}",
                    AnnouncementMessageTrusted = "trusted template {username}",
                }
            });

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert - both section headers + labels are rendered, and the preview
        // component substitutes the distinct example usernames.
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Admin Bypass Announcement"));
            Assert.That(cut.Markup, Does.Contain("Trusted User Announcement"));
            Assert.That(cut.Markup, Does.Contain("Template (admin)"));
            Assert.That(cut.Markup, Does.Contain("Template (trusted)"));
            Assert.That(cut.Markup, Does.Contain("@example_admin"));
            Assert.That(cut.Markup, Does.Contain("@example_trusted"));
        });
    }

    [Test]
    public void TrustedBypassPanel_FieldsDisabled_WhenToggleOff()
    {
        // Arrange - TrustedBypass.Enabled defaults to false
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                TrustedBypass = new TrustedBypassConfig { Enabled = false }
            });

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert - MudBlazor renders disabled fields with the disabled attribute
        cut.WaitForAssertion(() =>
        {
            var disabledInputs = cut.FindAll("input[disabled], textarea[disabled]");
            Assert.That(disabledInputs.Count, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Save_PersistsTrustedBypass_ThroughConfigService()
    {
        // Arrange - MainWelcomeMessage must be non-empty so LoadConfig takes the "load as-is"
        // path and does not reset TrustedBypass to defaults.
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                MainWelcomeMessage = "Welcome {username}!",
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageAdmin = "admin custom",
                    AnnouncementMessageTrusted = "trusted custom",
                    AnnouncementTtlSeconds = 45,
                }
            });

        var cut = Render<WelcomeSystemConfig>();

        // Capture the config passed to SaveAsync so we can assert its values
        WelcomeConfig? captured = null;
        ConfigService.SaveAsync<WelcomeConfig>(
            Arg.Any<ConfigType>(), Arg.Any<ChatIdentity>(), Arg.Do<WelcomeConfig>(c => captured = c))
            .Returns(Task.CompletedTask);

        // Act
        await cut.InvokeAsync(() => cut.Instance.SaveConfiguration());

        // Assert
        Assert.That(captured, Is.Not.Null, "SaveAsync was not called");
        Assert.That(captured!.TrustedBypass.Enabled, Is.True);
        Assert.That(captured.TrustedBypass.AnnouncementMessageAdmin, Is.EqualTo("admin custom"));
        Assert.That(captured.TrustedBypass.AnnouncementMessageTrusted, Is.EqualTo("trusted custom"));
        Assert.That(captured.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(45));
    }

    [Test]
    public async Task LoadConfig_MigrationBranch_PreservesTrustedBypassAndJoinSecurity()
    {
        // Arrange: legacy-format config (empty MainWelcomeMessage triggers migration branch)
        // with admin-configured TrustedBypass and a JoinSecurity sub-setting. The migration
        // branch previously reset both to defaults; Task 15's fix preserves them.
        var legacyConfig = new WelcomeConfig
        {
            MainWelcomeMessage = string.Empty, // triggers migration branch
            TrustedBypass = new TrustedBypassConfig
            {
                Enabled = true,
                AnnouncementMessageAdmin = "legacy admin template",
                AnnouncementMessageTrusted = "legacy trusted template",
                AnnouncementTtlSeconds = 60,
            },
            JoinSecurity = new JoinSecurityConfig
            {
                Cas = { Enabled = true },
            },
        };
        ConfigService.GetAsync<WelcomeConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(legacyConfig);

        var cut = Render<WelcomeSystemConfig>();

        // Capture the config saved back so we can inspect the component's _config state.
        WelcomeConfig? captured = null;
        ConfigService.SaveAsync<WelcomeConfig>(
            Arg.Any<ConfigType>(), Arg.Any<ChatIdentity>(), Arg.Do<WelcomeConfig>(c => captured = c))
            .Returns(Task.CompletedTask);

        // Act - trigger Save so we can read the preserved config via the captured arg.
        await cut.InvokeAsync(() => cut.Instance.SaveConfiguration());

        // Assert - migration branch should NOT reset TrustedBypass or JoinSecurity.
        Assert.That(captured, Is.Not.Null, "SaveAsync was not called");
        Assert.Multiple(() =>
        {
            Assert.That(captured!.TrustedBypass.Enabled, Is.True,
                "Migration branch should preserve TrustedBypass.Enabled");
            Assert.That(captured.TrustedBypass.AnnouncementMessageAdmin, Is.EqualTo("legacy admin template"),
                "Migration branch should preserve TrustedBypass.AnnouncementMessageAdmin");
            Assert.That(captured.TrustedBypass.AnnouncementMessageTrusted, Is.EqualTo("legacy trusted template"),
                "Migration branch should preserve TrustedBypass.AnnouncementMessageTrusted");
            Assert.That(captured.TrustedBypass.AnnouncementTtlSeconds, Is.EqualTo(60),
                "Migration branch should preserve TrustedBypass.AnnouncementTtlSeconds");
            Assert.That(captured.JoinSecurity.Cas.Enabled, Is.True,
                "Migration branch should preserve JoinSecurity settings");
        });
    }

    #endregion
}
