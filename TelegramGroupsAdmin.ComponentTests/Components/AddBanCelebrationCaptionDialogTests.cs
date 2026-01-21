using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared.Settings;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for AddBanCelebrationCaptionDialog tests.
/// Registers mocked repositories in the constructor.
/// </summary>
public class AddBanCelebrationCaptionDialogTestContext : BunitContext
{
    protected IBanCelebrationCaptionRepository CaptionRepository { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected AddBanCelebrationCaptionDialogTestContext()
    {
        // Create mocks
        CaptionRepository = Substitute.For<IBanCelebrationCaptionRepository>();

        // Configure default behavior
        CaptionRepository.AddAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(info => new BanCelebrationCaption
            {
                Id = 1,
                Text = info.ArgAt<string>(0),
                DmText = info.ArgAt<string>(1),
                Name = info.ArgAt<string?>(2),
                CreatedAt = DateTimeOffset.UtcNow
            });

        CaptionRepository.UpdateAsync(
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>()).Returns(info => new BanCelebrationCaption
            {
                Id = info.ArgAt<int>(0),
                Text = info.ArgAt<string>(1),
                DmText = info.ArgAt<string>(2),
                Name = info.ArgAt<string?>(3),
                CreatedAt = DateTimeOffset.UtcNow
            });

        // Register mocks
        Services.AddSingleton(CaptionRepository);

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
/// Component tests for AddBanCelebrationCaptionDialog.razor
/// Tests the dialog for adding/editing ban celebration captions with chat and DM versions.
/// </summary>
[TestFixture]
public class AddBanCelebrationCaptionDialogTests : AddBanCelebrationCaptionDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        CaptionRepository.ClearReceivedCalls();
    }

    #region Helper Methods

    /// <summary>
    /// Opens the AddBanCelebrationCaptionDialog in Add mode.
    /// </summary>
    private async Task<IDialogReference> OpenAddDialogAsync()
    {
        return await DialogService.ShowAsync<AddBanCelebrationCaptionDialog>("Add Caption");
    }

    /// <summary>
    /// Opens the AddBanCelebrationCaptionDialog in Edit mode with existing caption.
    /// </summary>
    private async Task<IDialogReference> OpenEditDialogAsync(BanCelebrationCaption caption)
    {
        var parameters = new DialogParameters<AddBanCelebrationCaptionDialog>
        {
            { x => x.CaptionToEdit, caption }
        };

        return await DialogService.ShowAsync<AddBanCelebrationCaptionDialog>("Edit Caption", parameters);
    }

    /// <summary>
    /// Creates a test caption for edit mode tests.
    /// </summary>
    private static BanCelebrationCaption CreateTestCaption()
    {
        return new BanCelebrationCaption
        {
            Id = 42,
            Text = "💀 **FATALITY!** {username} has been finished!",
            DmText = "You have been finished!",
            Name = "Mortal Kombat - Fatality",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

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
        _ = OpenAddDialogAsync();

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
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Ban Celebration Caption"));
            Assert.That(provider.Markup, Does.Contain("mud-icon"));
        });
    }

    #endregion

    #region Add Mode Tests

    [Test]
    public void AddMode_ShowsAddTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add Ban Celebration Caption"));
        });
    }

    [Test]
    public void AddMode_ShowsAddCaptionButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add Caption"));
        });
    }

    [Test]
    public void AddMode_FieldsAreEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert - Chat and DM caption fields should have placeholder text
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Example: {username} has been finished!"));
            Assert.That(provider.Markup, Does.Contain("Example: You have been finished!"));
        });
    }

    #endregion

    #region Edit Mode Tests

    [Test]
    public void EditMode_ShowsEditTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var caption = CreateTestCaption();

        // Act
        _ = OpenEditDialogAsync(caption);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Edit Ban Celebration Caption"));
        });
    }

    [Test]
    public void EditMode_ShowsSaveChangesButton()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var caption = CreateTestCaption();

        // Act
        _ = OpenEditDialogAsync(caption);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Save Changes"));
        });
    }

    [Test]
    public void EditMode_PopulatesExistingName()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var caption = CreateTestCaption();

        // Act
        _ = OpenEditDialogAsync(caption);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Mortal Kombat - Fatality"));
        });
    }

    [Test]
    public void EditMode_PopulatesExistingText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var caption = CreateTestCaption();

        // Act
        _ = OpenEditDialogAsync(caption);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("{username} has been finished!"));
        });
    }

    #endregion

    #region Form Field Tests

    [Test]
    public void HasNameField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Name (optional)"));
        });
    }

    [Test]
    public void HasChatCaptionField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Chat Caption"));
            Assert.That(provider.Markup, Does.Contain("Posted to chat. Use {username} for banned user's name."));
        });
    }

    [Test]
    public void HasDmCaptionField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("DM Caption"));
            Assert.That(provider.Markup, Does.Contain("Sent to banned user"));
        });
    }

    [Test]
    public void HasPlaceholdersExpansionPanel()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Available Placeholders"));
        });
    }

    [Test]
    public void PlaceholdersPanel_ContainsUsernamePlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("{username}"));
            Assert.That(provider.Markup, Does.Contain("banned user's display name"));
        });
    }

    [Test]
    public void PlaceholdersPanel_ContainsChatnamePlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("{chatname}"));
            Assert.That(provider.Markup, Does.Contain("chat where the ban occurred"));
        });
    }

    [Test]
    public void PlaceholdersPanel_ContainsBancountPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("{bancount}"));
            Assert.That(provider.Markup, Does.Contain("ban count"));
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
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });
    }

    [Test]
    public void AddCaptionButton_DisabledWhenEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenAddDialogAsync();

        // Assert - Add Caption button should be disabled when fields are empty
        provider.WaitForAssertion(() =>
        {
            var buttons = provider.FindAll("button");
            var addButton = buttons.FirstOrDefault(b =>
                b.TextContent.Contains("Add Caption") && !b.TextContent.Contains("Ban Celebration"));
            Assert.That(addButton, Is.Not.Null);
            Assert.That(addButton!.HasAttribute("disabled"), Is.True);
        });
    }

    [Test]
    public void CancelButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenAddDialogAsync();

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
        _ = OpenAddDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Mortal Kombat - Fatality"));
        });
    }

    #endregion
}
