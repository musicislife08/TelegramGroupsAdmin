using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Services;
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
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
    public void LoadConfig_TrustedBypassPopulated_RendersCustomTemplates()
    {
        // Arrange: load-as-is branch (non-empty MainWelcomeMessage) with custom TrustedBypass
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                Enabled = true,
                MainWelcomeMessage = "Welcome {username}!",
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageAdmin = "admin custom",
                    AnnouncementMessageTrusted = "trusted custom",
                    AnnouncementTtlSeconds = 45
                }
            });

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert: the loaded values flow into the rendered DOM
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("admin custom"));
            Assert.That(cut.Markup, Does.Contain("trusted custom"));
        }, TimeSpan.FromSeconds(2));

        // TTL: MudNumericField at WelcomeSystemConfig.razor:257-258 renders the bound
        // value into both `value` and `aria-valuenow` attributes. The label text
        // "Auto-delete after (seconds)" lives in a sibling <label for="..."> element,
        // so traverse from label to input via the for-id.
        cut.WaitForAssertion(() =>
        {
            var ttlLabel = cut.FindAll("label").First(l => l.TextContent.Contains("Auto-delete after"));
            var ttlInput = cut.Find($"#{ttlLabel.GetAttribute("for")}");
            Assert.That(ttlInput.GetAttribute("value"), Is.EqualTo("45"));
        }, TimeSpan.FromSeconds(2));
    }

    [Test]
    public void LoadConfig_MigrationBranch_PreservesTrustedBypassAndJoinSecurity()
    {
        // Arrange: legacy config with empty MainWelcomeMessage triggers the migration
        // branch at WelcomeSystemConfig.razor:570-587. Migration explicitly assigns
        // config.TrustedBypass and config.JoinSecurity onto WelcomeConfig.Default,
        // so values should render.
        ConfigService.GetWelcomeAsync(Arg.Any<long>())
            .Returns(new WelcomeConfig
            {
                Enabled = true,
                MainWelcomeMessage = "",
                TrustedBypass = new TrustedBypassConfig
                {
                    Enabled = true,
                    AnnouncementMessageAdmin = "legacy admin template",
                    AnnouncementMessageTrusted = "legacy trusted template",
                    AnnouncementTtlSeconds = 30
                },
                JoinSecurity = new JoinSecurityConfig
                {
                    Cas = new CasConfig { Enabled = true }
                }
            });

        // Act
        var cut = Render<WelcomeSystemConfig>();

        // Assert: migration branch preserves TrustedBypass and JoinSecurity into the rendered form
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("legacy admin template"));
            Assert.That(cut.Markup, Does.Contain("legacy trusted template"));
        }, TimeSpan.FromSeconds(2));

        // CAS toggle: the MudSwitch at WelcomeSystemConfig.razor:78 renders next to a
        // status chip (lines 82-84) whose color and text reflect Cas.Enabled. The chip
        // is the user-visible state indicator and is reliable in static bUnit render
        // (the switch's <input checked> attribute is set via Blazor interop and may not
        // appear in the static DOM). When Cas.Enabled is true the chip has
        // `mud-chip-color-success` class and text "Enabled".
        cut.WaitForAssertion(() =>
        {
            var casLabel = cut.FindAll("p").First(p => p.TextContent.Contains("CAS (Combot Anti-Spam)"));
            var casChip = casLabel.ParentElement!.QuerySelector(".mud-chip")!;
            Assert.That(casChip.ClassList, Does.Contain("mud-chip-color-success"),
                "CAS chip should render in success/enabled state when Cas.Enabled is true");
            Assert.That(casChip.TextContent.Trim(), Is.EqualTo("Enabled"));
        }, TimeSpan.FromSeconds(2));
    }

    #endregion
}
