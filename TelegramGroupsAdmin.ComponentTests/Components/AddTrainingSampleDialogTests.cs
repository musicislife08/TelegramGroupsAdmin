using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Services.AI;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Base context for AddTrainingSampleDialog tests that registers mocked services.
/// bUnit 2.x requires all services to be registered before the first Render() call.
/// </summary>
public class AddTrainingSampleDialogTestContext : BunitContext
{
    protected IAITranslationService TranslationService { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected AddTrainingSampleDialogTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        TranslationService = Substitute.For<IAITranslationService>();

        // Register mocks
        Services.AddSingleton(TranslationService);

        // THEN add MudBlazor services (includes ISnackbar)
        Services.AddMudServices(options =>
        {
            options.PopoverOptions.ThrowOnDuplicateProvider = false;
            options.PopoverOptions.CheckForPopoverProvider = false;
        });

        // Set up JSInterop - use SetVoidResult() for void methods
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("mudPopover.initialize", _ => true).SetVoidResult();
        JSInterop.SetupVoid("mudPopover.connect", _ => true).SetVoidResult();
        JSInterop.Setup<int>("mudpopoverHelper.countProviders").SetResult(1);
    }

    /// <summary>
    /// Renders the MudDialogProvider and gets the dialog service.
    /// Must be called at the start of each test.
    /// </summary>
    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for AddTrainingSampleDialog.razor
/// Tests the dialog for manually adding training samples to the Bayes classifier.
/// </summary>
[TestFixture]
public class AddTrainingSampleDialogTests : AddTrainingSampleDialogTestContext
{
    /// <summary>
    /// Clear mock received calls before each test to ensure test isolation.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        TranslationService.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Opens the AddTrainingSampleDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync()
    {
        var parameters = new DialogParameters<AddTrainingSampleDialog>();
        return await DialogService.ShowAsync<AddTrainingSampleDialog>("Add Training Sample", parameters);
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
    public void HasTwoButtons()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
            Assert.That(provider.Markup, Does.Contain("Add Sample"));
        });
    }

    #endregion

    #region Message Text Field Tests

    [Test]
    public void HasOriginalMessageField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Message Text (Original)"));
        });
    }

    [Test]
    public void DisplaysOriginalMessageHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("The original message text (any language)"));
        });
    }

    [Test]
    public void HasTranslateButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Auto-translate to English"));
        });
    }

    #endregion

    #region Translated Text Field Tests

    [Test]
    public void HasTranslatedTextField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Translated Text (English)"));
        });
    }

    [Test]
    public void DisplaysTranslatedTextHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Leave empty if message is already in English"));
        });
    }

    #endregion

    #region Classification Tests

    [Test]
    public void DisplaysClassificationSection()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Classification:"));
        });
    }

    [Test]
    public void DisplaysSpamOption()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("SPAM - This message is spam/unwanted"));
        });
    }

    [Test]
    public void DisplaysHamOption()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("HAM - This message is legitimate/wanted"));
        });
    }

    [Test]
    public void HasRadioGroup()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-radio-group"));
        });
    }

    #endregion

    #region Source Select Tests

    [Test]
    public void HasSourceSelect()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Source"));
            Assert.That(provider.Markup, Does.Contain("mud-select"));
        });
    }

    // Note: MudSelect items are rendered via popover which requires JS interop.
    // Testing specific select options is better suited for Playwright E2E tests.

    #endregion

    #region Training Tips Tests

    [Test]
    public void DisplaysTrainingTips()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Training Tips:"));
        });
    }

    [Test]
    public void DisplaysSpamExamples()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Promotional messages, scams, crypto ads"));
        });
    }

    [Test]
    public void DisplaysHamExamples()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Normal conversations, legitimate announcements"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void AddButtonHasPrimaryColor()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-button-filled-primary"));
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
