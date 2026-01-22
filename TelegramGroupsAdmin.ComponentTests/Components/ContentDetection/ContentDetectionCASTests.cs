using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.ContentDetection;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.ContentDetection;
using TelegramGroupsAdmin.Configuration.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components.ContentDetection;

/// <summary>
/// Test context for ContentDetectionCAS tests.
/// Registers mocked IConfigService and ISnackbar.
/// </summary>
public class ContentDetectionCASTestContext : BunitContext
{
    protected IConfigService ConfigService { get; }
    protected ISnackbar Snackbar { get; }

    protected ContentDetectionCASTestContext()
    {
        // Create mocks
        ConfigService = Substitute.For<IConfigService>();
        Snackbar = Substitute.For<ISnackbar>();

        // Default config returns - global config with 5 second timeout
        var defaultConfig = new ContentDetectionConfig
        {
            Cas = new CasConfig
            {
                Enabled = true,
                Timeout = TimeSpan.FromSeconds(5),
                ApiUrl = "https://api.cas.chat"
            }
        };

        ConfigService.GetAsync<ContentDetectionConfig>(ConfigType.ContentDetection, Arg.Any<long>())
            .Returns(defaultConfig);

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
/// Component tests for ContentDetectionCAS.razor.
/// Tests the CAS (Combot Anti-Spam) configuration component.
/// </summary>
[TestFixture]
public class ContentDetectionCASTests : ContentDetectionCASTestContext
{
    [SetUp]
    public void Setup()
    {
        ConfigService.ClearReceivedCalls();

        // Reset to default config for each test
        var defaultConfig = new ContentDetectionConfig
        {
            Cas = new CasConfig
            {
                Enabled = true,
                Timeout = TimeSpan.FromSeconds(5),
                ApiUrl = "https://api.cas.chat"
            }
        };

        ConfigService.GetAsync<ContentDetectionConfig>(ConfigType.ContentDetection, Arg.Any<long>())
            .Returns(defaultConfig);
    }

    #region Structure Tests

    [Test]
    public void Renders_WithoutError()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        Assert.That(cut.Markup, Is.Not.Empty);
    }

    [Test]
    public void Renders_DisplaysTitle()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("CAS (Combot Anti-Spam)"));
        });
    }

    [Test]
    public void Renders_HasEnableSwitch()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Enable CAS Check"));
        });
    }

    [Test]
    public void Renders_HasTimeoutField()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Timeout (seconds)"));
        });
    }

    [Test]
    public void Renders_HasApiUrlField()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("CAS API URL"));
        });
    }

    [Test]
    public void Renders_HasUserAgentField()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("User Agent"));
        });
    }

    #endregion

    #region Timeout Binding Tests

    [Test]
    public void TimeoutField_DisplaysInitialValue()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert - Should display the initial 5 second timeout
        cut.WaitForAssertion(() =>
        {
            var inputs = cut.FindAll("input[type='number']");
            var timeoutInput = inputs.FirstOrDefault(i =>
                cut.Markup.Contains("Timeout (seconds)"));

            Assert.That(timeoutInput, Is.Not.Null, "Timeout input should exist");
            Assert.That(timeoutInput!.GetAttribute("value"), Is.EqualTo("5"),
                "Initial timeout should be 5 seconds");
        });
    }

    [Test]
    public void TimeoutField_ChangingValue_UpdatesConfig()
    {
        // Arrange
        var cut = Render<ContentDetectionCAS>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Timeout (seconds)"));
        });

        // Act - Find the timeout input and change its value
        var timeoutInput = cut.Find("input[type='number']");
        timeoutInput.Change("15");

        // Assert - GetConfig should return the updated timeout
        var config = cut.Instance.GetConfig();
        Assert.That(config.Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(15)),
            "Changing timeout input should update the config's Timeout property");
    }

    [Test]
    public void TimeoutField_ChangingValue_PersistsAcrossMultipleChanges()
    {
        // Arrange
        var cut = Render<ContentDetectionCAS>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Timeout (seconds)"));
        });

        var timeoutInput = cut.Find("input[type='number']");

        // Act & Assert - Change value multiple times, verify after each change
        // Note: GetConfig() returns the same object reference, so we must assert immediately
        timeoutInput.Change("10");
        Assert.That(cut.Instance.GetConfig().Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)),
            "First change to 10 should persist");

        timeoutInput.Change("20");
        Assert.That(cut.Instance.GetConfig().Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(20)),
            "Second change to 20 should persist");

        timeoutInput.Change("30");
        Assert.That(cut.Instance.GetConfig().Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(30)),
            "Third change to 30 should persist");
    }

    [Test]
    public void TimeoutField_DefaultValue_IsFiveSeconds()
    {
        // Arrange & Act
        var cut = Render<ContentDetectionCAS>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            var config = cut.Instance.GetConfig();
            Assert.That(config.Cas.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)),
                "Default timeout should be 5 seconds");
        });
    }

    #endregion

    #region Chat-Specific Mode Tests

    [Test]
    public void ChatMode_ShowsUseGlobalToggle()
    {
        // Arrange & Act - Render with a ChatId (non-global mode)
        var cut = Render<ContentDetectionCAS>(p => p
            .Add(x => x.ChatId, 123456L));

        // Assert
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Using Global Settings").Or.Contain("Using Custom Settings"),
                "Should show the UseGlobal toggle in chat mode");
        });
    }

    [Test]
    public void GlobalMode_HidesUseGlobalToggle()
    {
        // Arrange & Act - Render without ChatId (global mode)
        var cut = Render<ContentDetectionCAS>();

        // Assert - Should not show the "Using Global/Custom Settings" toggle
        cut.WaitForAssertion(() =>
        {
            // The override toggle section should not be visible in global mode
            Assert.That(cut.Markup, Does.Not.Contain("Using Custom Settings"));
        });
    }

    #endregion
}
