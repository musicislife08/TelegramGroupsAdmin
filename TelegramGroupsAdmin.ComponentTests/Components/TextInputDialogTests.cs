using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TextInputDialog.razor
/// Tests the text input dialog with configurable label, max length, and line count.
/// </summary>
[TestFixture]
public class TextInputDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the TextInputDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        string? title = null,
        string? label = null,
        int? maxLength = null,
        int? lines = null)
    {
        var parameters = new DialogParameters<TextInputDialog>();
        if (title != null) parameters.Add(x => x.Title, title);
        if (label != null) parameters.Add(x => x.Label, label);
        if (maxLength != null) parameters.Add(x => x.MaxLength, maxLength.Value);
        if (lines != null) parameters.Add(x => x.Lines, lines.Value);

        return await DialogService.ShowAsync<TextInputDialog>(title ?? "Input", parameters);
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
    public void HasTextField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-input-control"));
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
            var buttons = provider.FindAll("button.mud-button-root");
            Assert.That(buttons.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void TextFieldHasOutlinedVariant()
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

    #endregion

    #region Label Parameter Tests

    [Test]
    public void DisplaysCustomLabel()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(label: "Enter your name");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Enter your name"));
        });
    }

    [Test]
    public void DisplaysDefaultLabel()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Default label is "Text"
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Text"));
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
    public void DisplaysSubmitButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Submit"));
        });
    }

    [Test]
    public void SubmitButtonHasPrimaryColor()
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

    #region Submit Button State Tests

    [Test]
    public void SubmitButtonIsDisabled_WhenInputEmpty()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Submit button should be disabled initially
        provider.WaitForAssertion(() =>
        {
            var submitButton = provider.FindAll("button").First(b => b.TextContent.Contains("Submit"));
            Assert.That(submitButton.HasAttribute("disabled"), Is.True);
        });
    }

    // Note: Testing Submit button enabled state after input requires MudTextField's
    // internal value binding to work, which needs complex event triggering in bUnit.
    // This is better suited for Playwright E2E tests that can interact with the real UI.

    #endregion

    #region MaxLength Parameter Tests

    [Test]
    public void DisplaysCounterWithMaxLength()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxLength: 100);

        // Assert - Should show counter with max length
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-input-helper-text"));
        });
    }

    [Test]
    public void DisplaysDefaultMaxLength()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Default max length is 500, shown in counter
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("0 / 500"));
        });
    }

    [Test]
    public void DisplaysCustomMaxLength()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxLength: 250);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("0 / 250"));
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
        var cancelButton = provider.FindAll("button").First(b => b.TextContent.Contains("Cancel"));
        cancelButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    // Note: Submit button click test requires entering text via MudTextField's
    // complex binding system, which is better tested in Playwright E2E tests.

    #endregion

    // Note: Testing actual dialog result values requires awaiting dialogRef.Result
    // which causes deadlocks in bUnit. Dialog close behavior is verified by checking
    // markup changes. Full result verification is better suited for Playwright E2E tests.
}
