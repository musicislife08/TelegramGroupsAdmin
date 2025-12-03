using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for PermissionDialog.razor
/// Tests the permission level selection dialog with Admin, GlobalAdmin, and Owner options.
/// </summary>
[TestFixture]
public class PermissionDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the PermissionDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        PermissionLevel? currentLevel = null,
        string? userEmail = null,
        PermissionLevel? maxPermissionLevel = null)
    {
        var parameters = new DialogParameters<PermissionDialog>();
        if (currentLevel != null) parameters.Add(x => x.CurrentPermissionLevel, currentLevel.Value);
        if (userEmail != null) parameters.Add(x => x.UserEmail, userEmail);
        if (maxPermissionLevel != null) parameters.Add(x => x.MaxPermissionLevel, maxPermissionLevel.Value);

        return await DialogService.ShowAsync<PermissionDialog>("Change Permission", parameters);
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
    public void HasRadioGroup()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-radio-group"));
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

    #endregion

    #region UserEmail Parameter Tests

    [Test]
    public void DisplaysUserEmail()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(userEmail: "test@example.com");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("test@example.com"));
        });
    }

    [Test]
    public void DisplaysChangePermissionMessage()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(userEmail: "user@test.com");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Change permission level for user@test.com"));
        });
    }

    #endregion

    #region Permission Level Options Tests

    [Test]
    public void DisplaysAdminOption()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Admin"));
            Assert.That(provider.Markup, Does.Contain("Chat-scoped moderation"));
        });
    }

    [Test]
    public void DisplaysGlobalAdminOption()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("GlobalAdmin"));
            Assert.That(provider.Markup, Does.Contain("Global moderation"));
        });
    }

    [Test]
    public void DisplaysOwnerOption_WhenMaxPermissionIsOwner()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.Owner);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Owner"));
            Assert.That(provider.Markup, Does.Contain("Full system access"));
        });
    }

    [Test]
    public void HidesOwnerOption_WhenMaxPermissionIsGlobalAdmin()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.GlobalAdmin);

        // Assert - Owner option should not be visible
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Full system access"));
        });
    }

    [Test]
    public void ShowsInfoAlert_WhenMaxPermissionIsGlobalAdmin()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.GlobalAdmin);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("mud-alert"));
            Assert.That(provider.Markup, Does.Contain("You can only assign Admin or GlobalAdmin permissions"));
        });
    }

    [Test]
    public void HidesInfoAlert_WhenMaxPermissionIsOwner()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.Owner);

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("You can only assign Admin or GlobalAdmin permissions"));
        });
    }

    #endregion

    #region Button Tests

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
    public void DisplaysSaveButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Save"));
        });
    }

    [Test]
    public void SaveButtonHasPrimaryColor()
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

    #region Radio Button Count Tests

    [Test]
    public void HasThreeRadioButtons_WhenOwnerCanAssign()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.Owner);

        // Assert
        provider.WaitForAssertion(() =>
        {
            var radios = provider.FindAll(".mud-radio");
            Assert.That(radios.Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void HasTwoRadioButtons_WhenGlobalAdminCanAssign()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(maxPermissionLevel: PermissionLevel.GlobalAdmin);

        // Assert
        provider.WaitForAssertion(() =>
        {
            var radios = provider.FindAll(".mud-radio");
            Assert.That(radios.Count, Is.EqualTo(2));
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
    public void SaveButton_ClosesDialog()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Save"));
        });

        // Act - Click save button
        var saveButton = provider.FindAll("button").First(b => b.TextContent.Contains("Save"));
        saveButton.Click();

        // Assert - Dialog content should be removed from markup
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("mud-dialog-content"));
        });
    }

    #endregion

    #region Default Values Tests

    [Test]
    public void DefaultsToOwnerMaxPermission()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act - Don't specify maxPermissionLevel, should default to Owner
        _ = OpenDialogAsync();

        // Assert - Owner option should be visible (default MaxPermissionLevel is Owner)
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Full system access"));
        });
    }

    #endregion

    // Note: Testing which radio button is selected based on CurrentPermissionLevel requires
    // inspecting MudBlazor's internal radio state, which is complex in bUnit.
    // Full selection and result verification is better suited for Playwright E2E tests.
}
