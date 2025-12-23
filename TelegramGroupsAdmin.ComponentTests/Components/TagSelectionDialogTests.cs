using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Test context for TagSelectionDialog tests.
/// Registers mocked ITagDefinitionsRepository.
/// </summary>
public class TagSelectionDialogTestContext : BunitContext
{
    protected ITagDefinitionsRepository TagDefinitionsRepository { get; }
    protected IDialogService DialogService { get; private set; } = null!;

    protected TagSelectionDialogTestContext()
    {
        // Create mocks
        TagDefinitionsRepository = Substitute.For<ITagDefinitionsRepository>();

        // Default: return empty tag list
        TagDefinitionsRepository.GetAllAsync().Returns([]);

        // Register mocks
        Services.AddSingleton(TagDefinitionsRepository);

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

    protected IRenderedComponent<MudDialogProvider> RenderDialogProvider()
    {
        var provider = Render<MudDialogProvider>();
        DialogService = Services.GetRequiredService<IDialogService>();
        return provider;
    }
}

/// <summary>
/// Component tests for TagSelectionDialog.razor
/// Tests the dialog for selecting tags to add to a user.
/// </summary>
[TestFixture]
public class TagSelectionDialogTests : TagSelectionDialogTestContext
{
    [SetUp]
    public void Setup()
    {
        TagDefinitionsRepository.ClearReceivedCalls();
        TagDefinitionsRepository.GetAllAsync().Returns([]);
    }

    #region Helper Methods

    private async Task<IDialogReference> OpenDialogAsync(List<string>? userAssignedTags = null)
    {
        var parameters = new DialogParameters<TagSelectionDialog>
        {
            { x => x.UserAssignedTags, userAssignedTags ?? [] }
        };
        return await DialogService.ShowAsync<TagSelectionDialog>("Select Tags", parameters);
    }

    private static TagDefinition CreateTestTag(string name = "TestTag", TagColor color = TagColor.Primary)
    {
        return new TagDefinition
        {
            TagName = name,
            Color = color,
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
    public void HasCancelButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
        });
    }

    [Test]
    public void HasAddButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add"));
        });
    }

    [Test]
    public void DisplaysInstructionText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Add tags to this user"));
        });
    }

    #endregion

    #region Create New Tag Tests

    [Test]
    public void HasCreateNewTagButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Create New Tag"));
        });
    }

    #endregion

    #region Empty State Tests

    [Test]
    public void ShowsEmptyAlert_WhenNoTagsAvailable()
    {
        // Arrange
        var provider = RenderDialogProvider();
        TagDefinitionsRepository.GetAllAsync().Returns([]);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No tags available"));
        });
    }

    [Test]
    public void ShowsAllAssignedAlert_WhenAllTagsAssigned()
    {
        // Arrange
        var provider = RenderDialogProvider();
        List<TagDefinition> tags = [CreateTestTag("ExistingTag")];
        TagDefinitionsRepository.GetAllAsync().Returns(tags);

        // Act - User already has the only tag
        _ = OpenDialogAsync(userAssignedTags: ["ExistingTag"]);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("All available tags are already assigned"));
        });
    }

    #endregion

    #region Tag Display Tests

    [Test]
    public void DisplaysAvailableTags()
    {
        // Arrange
        var provider = RenderDialogProvider();
        List<TagDefinition> tags =
        [
            CreateTestTag("Spammer"),
            CreateTestTag("VIP")
        ];
        TagDefinitionsRepository.GetAllAsync().Returns(tags);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Spammer"));
            Assert.That(provider.Markup, Does.Contain("VIP"));
        });
    }

    [Test]
    public void FiltersOutAlreadyAssignedTags()
    {
        // Arrange
        var provider = RenderDialogProvider();
        List<TagDefinition> tags =
        [
            CreateTestTag("Assigned"),
            CreateTestTag("Available")
        ];
        TagDefinitionsRepository.GetAllAsync().Returns(tags);

        // Act - User has "Assigned" tag
        _ = OpenDialogAsync(userAssignedTags: ["Assigned"]);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain(">Assigned<")); // Not in a chip
            Assert.That(provider.Markup, Does.Contain("Available"));
        });
    }

    [Test]
    public void DisplaysSelectionInstructions()
    {
        // Arrange
        var provider = RenderDialogProvider();
        List<TagDefinition> tags = [CreateTestTag()];
        TagDefinitionsRepository.GetAllAsync().Returns(tags);

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Click tags to select"));
        });
    }

    #endregion

    #region Button State Tests

    [Test]
    public void AddButtonDisabled_WhenNoSelection()
    {
        // Arrange
        var provider = RenderDialogProvider();
        List<TagDefinition> tags = [CreateTestTag()];
        TagDefinitionsRepository.GetAllAsync().Returns(tags);

        // Act
        _ = OpenDialogAsync();

        // Assert - Add button should be disabled when no tags selected
        provider.WaitForAssertion(() =>
        {
            var addButton = provider.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Add"));
            Assert.That(addButton, Is.Not.Null);
            Assert.That(addButton!.GetAttribute("disabled"), Is.Not.Null);
        });
    }

    #endregion

    #region Button Tests

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
