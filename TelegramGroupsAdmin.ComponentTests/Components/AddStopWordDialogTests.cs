using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.ContentDetection.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Base context for AddStopWordDialog tests that registers mocked IStopWordsRepository.
/// bUnit 2.x requires all services to be registered before the first Render() call.
/// </summary>
public class AddStopWordDialogTestContext : BunitContext
{
    protected IStopWordsRepository StopWordsRepository { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected AddStopWordDialogTestContext()
    {
        // Create mocks FIRST (before AddMudServices locks the container)
        StopWordsRepository = Substitute.For<IStopWordsRepository>();

        // Default: words don't exist
        StopWordsRepository.ExistsAsync(Arg.Any<string>()).Returns(false);

        // Register mocks
        Services.AddSingleton(StopWordsRepository);

        // THEN add MudBlazor services
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
/// Component tests for AddStopWordDialog.razor
/// Tests the dialog for adding new stop words to the spam filter.
/// </summary>
[TestFixture]
public class AddStopWordDialogTests : AddStopWordDialogTestContext
{
    /// <summary>
    /// Clear mock received calls before each test to ensure test isolation.
    /// </summary>
    [SetUp]
    public void Setup()
    {
        StopWordsRepository.ClearReceivedCalls();
        // Reset default behavior
        StopWordsRepository.ExistsAsync(Arg.Any<string>()).Returns(false);
    }

    #region Helper Methods

    /// <summary>
    /// Opens the AddStopWordDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync()
    {
        var parameters = new DialogParameters<AddStopWordDialog>();
        return await DialogService.ShowAsync<AddStopWordDialog>("Add Stop Word", parameters);
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
            Assert.That(provider.Markup, Does.Contain("Add Stop Word"));
        });
    }

    #endregion

    #region Stop Word Field Tests

    [Test]
    public void HasStopWordField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Stop Word"));
        });
    }

    [Test]
    public void DisplaysStopWordHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Words are case-insensitive"));
        });
    }

    [Test]
    public void HasStopWordPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Enter word or phrase"));
        });
    }

    #endregion

    #region Notes Field Tests

    [Test]
    public void HasNotesField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Notes (Optional)"));
        });
    }

    [Test]
    public void DisplaysNotesPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Context about why this word was added"));
        });
    }

    [Test]
    public void DisplaysNotesHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Helpful for understanding the purpose"));
        });
    }

    #endregion

    #region Examples Section Tests

    [Test]
    public void DisplaysExamplesSection()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Examples:"));
        });
    }

    [Test]
    public void DisplaysExampleWords()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("bitcoin"));
            Assert.That(provider.Markup, Does.Contain("crypto"));
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
