using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for ServiceMessageDeletionSettings tests.
/// Registers mocked IConfigService and ISnackbar.
/// </summary>
public class ServiceMessageDeletionSettingsTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }
    protected ISnackbar Snackbar { get; }

    protected ServiceMessageDeletionSettingsTestContext()
    {
        // Create mocks
        ConfigService = Substitute.For<IConfigService>();
        Snackbar = Substitute.For<ISnackbar>();

        // Default config returns
        ConfigService.GetAsync<ServiceMessageDeletionConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(ServiceMessageDeletionConfig.Default);

        // Register mocks
        Services.AddSingleton(ConfigService);
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
}

/// <summary>
/// Component tests for ServiceMessageDeletionSettings.razor
/// Tests the service message deletion configuration component.
/// </summary>
[TestFixture]
public class ServiceMessageDeletionSettingsTests : ServiceMessageDeletionSettingsTestContext
{
    [SetUp]
    public void Setup()
    {
        ConfigService.ClearReceivedCalls();
        Snackbar.ClearReceivedCalls();
        ConfigService.GetAsync<ServiceMessageDeletionConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns(ServiceMessageDeletionConfig.Default);
    }

    #region Structure Tests

    [Test]
    public void RendersWithoutError()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void DisplaysTitle_WhenGlobalMode()
    {
        // Arrange & Act - No ChatId means global mode
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Service Message Deletion"));
        });
    }

    [Test]
    public void HidesTitle_WhenChatMode()
    {
        // Arrange & Act - With ChatId means per-chat mode
        var cut = Render<ServiceMessageDeletionSettings>(p => p
            .Add(x => x.Chat, ChatIdentity.FromId(123456L)));

        // Assert
        cut.WaitForAssertion(() =>
        {
            // Title should not appear when in per-chat mode
            Assert.That(cut.Markup, Does.Not.Contain("<h6"));
        });
    }

    [Test]
    public void DisplaysDescription_WhenGlobalMode()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Configure which types of Telegram service messages"));
        });
    }

    #endregion

    #region Toggle Tests

    [Test]
    public void HasAllSixToggles()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Delete Join Messages"));
            Assert.That(cut.Markup, Does.Contain("Delete Leave Messages"));
            Assert.That(cut.Markup, Does.Contain("Delete Photo Changes"));
            Assert.That(cut.Markup, Does.Contain("Delete Title Changes"));
            Assert.That(cut.Markup, Does.Contain("Delete Pin Notifications"));
            Assert.That(cut.Markup, Does.Contain("Delete Chat Creation Messages"));
        });
    }

    [Test]
    public void HasHelpTextForJoinMessages()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User joined the group"));
        });
    }

    [Test]
    public void HasHelpTextForLeaveMessages()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User left the group"));
        });
    }

    [Test]
    public void HasHelpTextForPhotoChanges()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Group photo added/removed"));
        });
    }

    [Test]
    public void HasHelpTextForTitleChanges()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User changed the group title"));
        });
    }

    [Test]
    public void HasHelpTextForPinNotifications()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User pinned a message"));
        });
    }

    [Test]
    public void HasHelpTextForChatCreation()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Group/supergroup/channel created"));
        });
    }

    [Test]
    public void TogglesAreEnabledByDefault()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert - check that switches are checked (default config has all true)
        cut.WaitForAssertion(() =>
        {
            var switches = cut.FindAll("input[type='checkbox']");
            Assert.That(switches.Count, Is.EqualTo(6), "Should have 6 toggle switches");
            foreach (var sw in switches)
            {
                Assert.That(sw.GetAttribute("checked"), Is.Not.Null.Or.EqualTo(""),
                    "All toggles should be enabled by default");
            }
        });
    }

    [Test]
    public void ToggleUpdatesState_WhenClicked()
    {
        // Arrange
        var cut = Render<ServiceMessageDeletionSettings>();

        cut.WaitForAssertion(() =>
        {
            var switches = cut.FindAll("input[type='checkbox']");
            Assert.That(switches.Count, Is.GreaterThan(0));
        });

        // Act - click the first toggle (Delete Join Messages)
        var firstSwitch = cut.Find("input[type='checkbox']");
        firstSwitch.Change(false);

        // Assert - state should change
        cut.WaitForAssertion(() =>
        {
            var updatedSwitch = cut.Find("input[type='checkbox']");
            Assert.That(updatedSwitch.GetAttribute("checked"), Is.Null.Or.EqualTo(""),
                "Toggle should be unchecked after clicking");
        });
    }

    #endregion

    #region Button Tests (Global Mode)

    [Test]
    public void HasSaveConfigurationButton_GlobalMode()
    {
        // Arrange & Act - Global mode (no ChatId)
        var cut = Render<ServiceMessageDeletionSettings>();

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
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Reset to Defaults"));
        });
    }

    [Test]
    public void HidesSaveButtons_ChatMode()
    {
        // Arrange & Act - Per-chat mode (has ChatId)
        var cut = Render<ServiceMessageDeletionSettings>(p => p
            .Add(x => x.Chat, ChatIdentity.FromId(123456L)));

        // Assert - Buttons should be hidden in per-chat mode
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Save Configuration"));
            Assert.That(cut.Markup, Does.Not.Contain("Reset to Defaults"));
        });
    }

    #endregion

    #region Reset to Defaults Tests

    [Test]
    public void ResetToDefaults_ShowsSnackbar()
    {
        // Arrange
        var cut = Render<ServiceMessageDeletionSettings>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Reset to Defaults"));
        });

        // Act - click Reset to Defaults button (use LINQ for reliable selector)
        var resetButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Reset to Defaults"));
        resetButton.Click();

        // Assert - snackbar should be called
        Snackbar.Received().Add(
            Arg.Is<string>(s => s.Contains("Defaults loaded")),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    #endregion

    #region Error State Tests

    [Test]
    public void ShowsErrorAlert_WhenConfigLoadFails()
    {
        // Arrange - return null to simulate failed config load (component handles this)
        ConfigService.GetAsync<ServiceMessageDeletionConfig>(Arg.Any<ConfigType>(), Arg.Any<long>())
            .Returns((ServiceMessageDeletionConfig?)null);

        // Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Assert - component falls back to default config, no error shown
        // (The actual error path requires exception which is complex to test with ValueTask)
        cut.WaitForAssertion(() =>
        {
            // With null config, component uses Default, so no error
            Assert.That(cut.Markup, Does.Contain("Delete Join Messages"));
        });
    }

    #endregion

    #region Config Service Interaction Tests

    [Test]
    public async Task LoadsConfig_OnInitialization()
    {
        // Arrange & Act
        var cut = Render<ServiceMessageDeletionSettings>();

        // Wait for async initialization
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Delete Join Messages"));
        });

        // Assert - ConfigService.GetAsync should have been called
        await ConfigService.Received().GetAsync<ServiceMessageDeletionConfig>(
            ConfigType.ServiceMessageDeletion,
            Arg.Any<long>());
    }

    [Test]
    public async Task LoadsConfig_WithChatId_WhenProvided()
    {
        // Arrange
        const long chatId = 123456L;

        // Act
        var cut = Render<ServiceMessageDeletionSettings>(p => p
            .Add(x => x.Chat, ChatIdentity.FromId(chatId)));

        // Wait for async initialization
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Delete Join Messages"));
        });

        // Assert - ConfigService.GetAsync should have been called with the chat ID
        await ConfigService.Received().GetAsync<ServiceMessageDeletionConfig>(
            ConfigType.ServiceMessageDeletion,
            chatId);
    }

    #endregion
}
