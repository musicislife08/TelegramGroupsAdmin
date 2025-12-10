using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for PassphrasePromptDialog.razor
/// Tests the passphrase input dialog for encrypted backup restoration.
/// </summary>
[TestFixture]
public class PassphrasePromptDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the PassphrasePromptDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync()
    {
        var parameters = new DialogParameters<PassphrasePromptDialog>();
        return await DialogService.ShowAsync<PassphrasePromptDialog>("Enter Passphrase", parameters);
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
            Assert.That(provider.Markup, Does.Contain("Decrypt"));
        });
    }

    [Test]
    public void HasTitleWithLockIcon()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Enter Backup Passphrase"));
            Assert.That(provider.Markup, Does.Contain("mud-icon"));
        });
    }

    #endregion

    #region Info Alert Tests

    [Test]
    public void DisplaysInfoAlert()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-alert"));
            Assert.That(provider.Markup, Does.Contain("This backup is encrypted"));
        });
    }

    [Test]
    public void DisplaysDecryptInstructions()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Enter the passphrase to decrypt and restore"));
        });
    }

    #endregion

    #region Passphrase Field Tests

    [Test]
    public void HasPassphraseField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Passphrase"));
            Assert.That(provider.Markup, Does.Contain("mud-input-control"));
        });
    }

    [Test]
    public void PassphraseFieldHasOutlinedVariant()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-input-outlined"));
        });
    }

    [Test]
    public void PassphraseFieldIsPasswordType()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Initially password type is hidden
        provider.WaitForAssertion(() =>
        {
            var input = provider.Find("input");
            Assert.That(input.GetAttribute("type"), Is.EqualTo("password"));
        });
    }

    [Test]
    public void PassphraseFieldHasVisibilityToggle()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Should have adornment for visibility toggle
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-input-adornment"));
        });
    }

    #endregion

    #region Button State Tests

    [Test]
    public void DecryptButtonIsDisabled_WhenPassphraseEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Decrypt button should be disabled initially
        provider.WaitForAssertion(() =>
        {
            var buttons = provider.FindAll("button");
            var decryptButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Decrypt"));
            Assert.That(decryptButton, Is.Not.Null);
            Assert.That(decryptButton!.HasAttribute("disabled"), Is.True);
        });
    }

    #endregion

    #region Button Text Tests

    [Test]
    public void DisplaysCancelButton()
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
    public void DisplaysDecryptRestoreButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Check for the button text (& is not escaped in button labels)
        provider.WaitForAssertion(() =>
        {
            var buttons = provider.FindAll("button");
            var decryptButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Decrypt") && b.TextContent.Contains("Restore"));
            Assert.That(decryptButton, Is.Not.Null);
        });
    }

    [Test]
    public void DecryptButtonHasPrimaryColor()
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

    #endregion

    #region Button Click Tests

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

    #region Visibility Toggle Tests

    [Test]
    public void ClickingVisibilityToggle_ChangesInputType()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            var input = provider.Find("input");
            Assert.That(input.GetAttribute("type"), Is.EqualTo("password"));
        });

        // Act - Click visibility toggle button
        var adornmentButton = provider.Find(".mud-input-adornment button");
        adornmentButton.Click();

        // Assert - Input type should change to text
        provider.WaitForAssertion(() =>
        {
            var input = provider.Find("input");
            Assert.That(input.GetAttribute("type"), Is.EqualTo("text"));
        });
    }

    [Test]
    public void ClickingVisibilityToggleTwice_ReturnsToPasswordType()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-input-adornment"));
        });

        // Act - Click visibility toggle twice
        var adornmentButton = provider.Find(".mud-input-adornment button");
        adornmentButton.Click();
        adornmentButton.Click();

        // Assert - Should be back to password type
        provider.WaitForAssertion(() =>
        {
            var input = provider.Find("input");
            Assert.That(input.GetAttribute("type"), Is.EqualTo("password"));
        });
    }

    #endregion

    // Note: Testing passphrase input and Enter key handling requires MudTextField's
    // binding to work properly, which is complex in bUnit. These scenarios are
    // better suited for Playwright E2E tests.
}
