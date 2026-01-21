using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for ImagePreviewDialog.razor
/// Tests the simple image preview dialog used for viewing ban celebration GIFs at full size.
/// </summary>
[TestFixture]
public class ImagePreviewDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the ImagePreviewDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        string imagePath = "ban-gifs/test.gif",
        string title = "Test Preview")
    {
        var parameters = new DialogParameters<ImagePreviewDialog>
        {
            { x => x.ImagePath, imagePath },
            { x => x.Title, title }
        };

        return await DialogService.ShowAsync<ImagePreviewDialog>(title, parameters);
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
    public void HasCloseButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });
    }

    #endregion

    #region Title Tests

    [Test]
    public void DisplaysTitle()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(title: "Ban Celebration Preview");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Ban Celebration Preview"));
        });
    }

    [Test]
    public void DisplaysDefaultTitle_WhenNotSpecified()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var parameters = new DialogParameters<ImagePreviewDialog>
        {
            { x => x.ImagePath, "test.gif" }
        };

        // Act
        _ = DialogService.ShowAsync<ImagePreviewDialog>("Test", parameters);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-dialog"));
        });
    }

    #endregion

    #region Image Tests

    [Test]
    public void DisplaysImage_WithCorrectPath()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(imagePath: "ban-gifs/celebration.gif");

        // Assert - The image source should include the media path
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("ban-gifs/celebration.gif"));
        });
    }

    [Test]
    public void Image_HasAltText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(title: "Celebration GIF");

        // Assert - Alt text should match title
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("alt=\"Celebration GIF\""));
        });
    }

    [Test]
    public void Image_HasStylingForMaxSize()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Should have max-width and max-height styling
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("max-width"));
            Assert.That(provider.Markup, Does.Contain("max-height"));
        });
    }

    #endregion

    #region Button Click Tests

    [Test]
    public void CloseButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Close"));
        });

        // Act - Click close button
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Contains("Close"));
        closeButton.Click();

        // Assert - Dialog content should be removed
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
