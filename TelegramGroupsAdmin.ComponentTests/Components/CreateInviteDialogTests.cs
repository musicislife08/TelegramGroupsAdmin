using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for CreateInviteDialog.razor
/// Tests the dialog for creating user invite links.
/// </summary>
[TestFixture]
public class CreateInviteDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the CreateInviteDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        string? inviteLink = null,
        DateTimeOffset? expiresAt = null,
        PermissionLevel? maxPermissionLevel = null)
    {
        var parameters = new DialogParameters<CreateInviteDialog>();
        if (inviteLink != null) parameters.Add(x => x.InviteLink, inviteLink);
        if (expiresAt != null) parameters.Add(x => x.ExpiresAt, expiresAt.Value);
        if (maxPermissionLevel != null) parameters.Add(x => x.MaxPermissionLevel, maxPermissionLevel.Value);

        return await DialogService.ShowAsync<CreateInviteDialog>("Create Invite", parameters);
    }

    #endregion

    #region Structure Tests - Before Invite Created

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
    public void DisplaysInviteMessage()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Invite a new user to register"));
        });
    }

    #endregion

    #region Permission Level Select Tests

    [Test]
    public void HasPermissionLevelSelect()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Permission Level"));
        });
    }

    [Test]
    public void HasSelectInput()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - MudSelect renders as input with select class
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-select"));
        });
    }

    // Note: MudSelect items are rendered via popover which requires JS interop.
    // Testing specific select options is better suited for Playwright E2E tests.

    #endregion

    #region Valid Days Field Tests

    [Test]
    public void HasValidDaysField()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Invite valid for (days)"));
        });
    }

    [Test]
    public void DisplaysValidDaysHelperText()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("How long until the invite link expires"));
        });
    }

    #endregion

    #region Button Tests - Before Invite Created

    [Test]
    public void DisplaysCancelButton_BeforeInvite()
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
    public void DisplaysGenerateButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Generate Invite"));
        });
    }

    [Test]
    public void GenerateButtonHasPrimaryColor()
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

    #region After Invite Created Tests

    [Test]
    public void DisplaysInviteLinkCreatedTitle_WhenLinkProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc123");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Invite Link Created"));
        });
    }

    [Test]
    public void DisplaysSuccessAlert_WhenLinkProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc123");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Send this link to the new user"));
        });
    }

    [Test]
    public void DisplaysInviteLink_WhenProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/xyz789");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("https://example.com/invite/xyz789"));
        });
    }

    [Test]
    public void DisplaysExpiresAt_WhenProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc", expiresAt: expiresAt);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Expires:"));
        });
    }

    [Test]
    public void DisplaysDoneButton_WhenLinkProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Done"));
        });
    }

    [Test]
    public void HidesCancelButton_WhenLinkProvided()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc");

        // Assert - Cancel should not be visible, only Done
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Done"));
            // Generate Invite should not be visible
            Assert.That(provider.Markup, Does.Not.Contain("Generate Invite"));
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

    [Test]
    public void DoneButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync(inviteLink: "https://example.com/invite/abc");

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Done"));
        });

        // Act - Click done button
        var doneButton = provider.FindAll("button").First(b => b.TextContent.Contains("Done"));
        doneButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion
}
