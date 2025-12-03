using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Models.Dialogs;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for EditStopWordDialog.razor
/// Tests the dialog for editing stop word notes.
/// </summary>
[TestFixture]
public class EditStopWordDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a test StopWordItem with the specified properties.
    /// </summary>
    private static StopWordItem CreateTestStopWord(
        long id = 1,
        string word = "spam",
        bool enabled = true,
        DateTimeOffset? addedDate = null,
        string? addedBy = null,
        string? notes = null)
    {
        return new StopWordItem
        {
            Id = id,
            Word = word,
            Enabled = enabled,
            AddedDate = addedDate ?? DateTimeOffset.UtcNow.AddDays(-7),
            AddedBy = addedBy,
            Notes = notes
        };
    }

    /// <summary>
    /// Opens the EditStopWordDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(StopWordItem stopWord)
    {
        var parameters = new DialogParameters<EditStopWordDialog>
        {
            { x => x.StopWord, stopWord }
        };

        return await DialogService.ShowAsync<EditStopWordDialog>("Edit Stop Word", parameters);
    }

    #endregion

    #region Structure Tests

    [Test]
    public void HasDialogContent()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

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
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

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
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
            Assert.That(provider.Markup, Does.Contain("Update"));
        });
    }

    #endregion

    #region StopWord Display Tests

    [Test]
    public void DisplaysStopWord()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord(word: "badword");

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("badword"));
        });
    }

    [Test]
    public void StopWordFieldIsDisabled()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Stop word text cannot be changed"));
        });
    }

    [Test]
    public void DisplaysStopWordLabel()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Stop Word"));
        });
    }

    #endregion

    #region Notes Field Tests

    [Test]
    public void HasNotesField()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Notes"));
        });
    }

    [Test]
    public void DisplaysNotesPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

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
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Update the notes for this stop word"));
        });
    }

    #endregion

    #region Metadata Display Tests

    [Test]
    public void DisplaysAddedBy()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord(addedBy: "admin@test.com");

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("By:"));
            Assert.That(provider.Markup, Does.Contain("admin@test.com"));
        });
    }

    [Test]
    public void DisplaysSystemWhenNoAddedBy()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord(addedBy: null);

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("System"));
        });
    }

    [Test]
    public void DisplaysAddedDate()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Added:"));
        });
    }

    #endregion

    #region Enabled Status Tests

    [Test]
    public void DisplaysEnabledChip_WhenEnabled()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord(enabled: true);

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Enabled"));
            Assert.That(provider.Markup, Does.Contain("mud-chip-color-success"));
        });
    }

    [Test]
    public void DisplaysDisabledChip_WhenDisabled()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord(enabled: false);

        // Act
        _ = OpenDialogAsync(stopWord);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Disabled"));
            Assert.That(provider.Markup, Does.Contain("mud-chip-color-error"));
        });
    }

    #endregion

    #region Button Tests

    [Test]
    public void UpdateButtonHasPrimaryColor()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();

        // Act
        _ = OpenDialogAsync(stopWord);

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
        var stopWord = CreateTestStopWord();
        _ = OpenDialogAsync(stopWord);

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

    [Test]
    public void UpdateButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var stopWord = CreateTestStopWord();
        _ = OpenDialogAsync(stopWord);

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Update"));
        });

        // Act - Click update button
        var updateButton = provider.FindAll("button").First(b => b.TextContent.Contains("Update") && !b.TextContent.Contains("Updating"));
        updateButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
