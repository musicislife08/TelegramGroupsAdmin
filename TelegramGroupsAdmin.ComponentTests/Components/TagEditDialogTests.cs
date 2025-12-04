using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TagEditDialog.razor
/// Tests the dialog for creating and editing user tags.
/// </summary>
[TestFixture]
public class TagEditDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the TagEditDialog for creating a new tag.
    /// </summary>
    private async Task<IDialogReference> OpenCreateDialogAsync()
    {
        var parameters = new DialogParameters<TagEditDialog>
        {
            { x => x.IsEdit, false }
        };

        return await DialogService.ShowAsync<TagEditDialog>("Create Tag", parameters);
    }

    /// <summary>
    /// Opens the TagEditDialog for editing an existing tag.
    /// </summary>
    private async Task<IDialogReference> OpenEditDialogAsync(
        string tagName = "test-tag",
        TagColor currentColor = TagColor.Primary)
    {
        var parameters = new DialogParameters<TagEditDialog>
        {
            { x => x.IsEdit, true },
            { x => x.TagName, tagName },
            { x => x.CurrentColor, currentColor }
        };

        return await DialogService.ShowAsync<TagEditDialog>("Edit Tag", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

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
        _ = OpenCreateDialogAsync();

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
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
            Assert.That(provider.Markup, Does.Contain("Create"));
        });
    }

    #endregion

    #region Create Mode Tests

    [Test]
    public void DisplaysTagNameField_InCreateMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Tag Name"));
            // Should not be disabled in create mode
            Assert.That(provider.Markup, Does.Contain("Lowercase letters, numbers, and hyphens only"));
        });
    }

    [Test]
    public void DisplaysPlaceholder_InCreateMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("e.g., translator, regional-admin, vip"));
        });
    }

    [Test]
    public void DisplaysInfoAlert_InCreateMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Tags are auto-created when first used"));
        });
    }

    [Test]
    public void DisplaysCreateButton_InCreateMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Create"));
        });
    }

    #endregion

    #region Edit Mode Tests

    [Test]
    public void DisplaysTagName_InEditMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenEditDialogAsync(tagName: "my-custom-tag");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("my-custom-tag"));
        });
    }

    [Test]
    public void TagNameFieldIsDisabled_InEditMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenEditDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Tag name cannot be changed"));
        });
    }

    [Test]
    public void HidesInfoAlert_InEditMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenEditDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Tags are auto-created when first used"));
        });
    }

    [Test]
    public void DisplaysUpdateButton_InEditMode()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenEditDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Update"));
        });
    }

    #endregion

    #region Color Select Tests

    [Test]
    public void HasColorSelect()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Color"));
            Assert.That(provider.Markup, Does.Contain("mud-select"));
        });
    }

    [Test]
    public void DisplaysColorHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Select a color for this tag"));
        });
    }

    // Note: MudSelect items are rendered via popover which requires JS interop.
    // Testing specific color options is better suited for Playwright E2E tests.

    #endregion

    #region Button Tests

    [Test]
    public void SubmitButtonHasPrimaryColor()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenCreateDialogAsync();

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
        _ = OpenCreateDialogAsync();

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
