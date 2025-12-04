using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for UserNotesDialog.razor
/// Tests the dialog that displays admin notes for a user.
/// </summary>
[TestFixture]
public class UserNotesDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Creates a test AdminNote with the specified properties.
    /// </summary>
    private static AdminNote CreateTestNote(
        long id = 1,
        string noteText = "Test note",
        bool isPinned = false,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        string createdByEmail = "admin@test.com")
    {
        return new AdminNote
        {
            Id = id,
            TelegramUserId = 12345,
            NoteText = noteText,
            IsPinned = isPinned,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt = updatedAt,
            CreatedBy = Actor.FromWebUser("user-123", createdByEmail)
        };
    }

    /// <summary>
    /// Opens the UserNotesDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        List<AdminNote>? notes = null,
        string? userName = null)
    {
        var parameters = new DialogParameters<UserNotesDialog>();
        if (notes != null) parameters.Add(x => x.Notes, notes);
        if (userName != null) parameters.Add(x => x.UserName, userName);

        return await DialogService.ShowAsync<UserNotesDialog>("User Notes", parameters);
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

    #region UserName Parameter Tests

    [Test]
    public void DisplaysUserName()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(userName: "TestUser");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Admin notes for TestUser"));
        });
    }

    [Test]
    public void DisplaysEmptyUserName()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(userName: "");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Admin notes for"));
        });
    }

    #endregion

    #region Empty Notes Tests

    [Test]
    public void ShowsInfoAlert_WhenNoNotes()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(notes: []);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-alert"));
            Assert.That(provider.Markup, Does.Contain("No notes found for this user"));
        });
    }

    [Test]
    public void ShowsInfoAlert_WhenNotesNull()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(notes: null);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("No notes found for this user"));
        });
    }

    #endregion

    #region Notes Display Tests

    [Test]
    public void DisplaysNoteText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(noteText: "This is my test note content")
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("This is my test note content"));
        });
    }

    [Test]
    public void DisplaysMultipleNotes()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(id: 1, noteText: "First note"),
            CreateTestNote(id: 2, noteText: "Second note"),
            CreateTestNote(id: 3, noteText: "Third note")
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("First note"));
            Assert.That(provider.Markup, Does.Contain("Second note"));
            Assert.That(provider.Markup, Does.Contain("Third note"));
        });
    }

    [Test]
    public void DisplaysCreatedBy()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(createdByEmail: "moderator@example.com")
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("moderator@example.com"));
        });
    }

    [Test]
    public void NotesAreInPaperCards()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote()
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-paper"));
        });
    }

    #endregion

    #region Pinned Notes Tests

    [Test]
    public void DisplaysPinnedChip_WhenNotePinned()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(isPinned: true)
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Pinned"));
            Assert.That(provider.Markup, Does.Contain("mud-chip"));
        });
    }

    [Test]
    public void HidesPinnedChip_WhenNoteNotPinned()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(isPinned: false, noteText: "Unpinned note")
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Unpinned note"));
            Assert.That(provider.Markup, Does.Not.Contain("Pinned"));
        });
    }

    [Test]
    public void PinnedChipHasWarningColor()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(isPinned: true)
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-chip-color-warning"));
        });
    }

    #endregion

    #region Updated Notes Tests

    [Test]
    public void ShowsEditedIndicator_WhenNoteUpdated()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(
                createdAt: DateTimeOffset.UtcNow.AddDays(-1),
                updatedAt: DateTimeOffset.UtcNow)
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("(edited"));
        });
    }

    [Test]
    public void HidesEditedIndicator_WhenNoteNotUpdated()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var notes = new List<AdminNote>
        {
            CreateTestNote(updatedAt: null)
        };

        // Act
        _ = OpenDialogAsync(notes: notes);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("(edited"));
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
        var closeButton = provider.FindAll("button").First(b => b.TextContent.Trim() == "Close");
        closeButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
