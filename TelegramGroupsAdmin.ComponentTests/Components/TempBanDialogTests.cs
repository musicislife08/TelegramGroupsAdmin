using Bunit;
using MudBlazor;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for TempBanDialog.razor
/// Tests the temporary ban dialog with duration selection and reason input.
/// </summary>
[TestFixture]
public class TempBanDialogTests : DialogTestContext
{
    #region Helper Methods

    /// <summary>
    /// Opens the TempBanDialog and returns the dialog reference.
    /// </summary>
    private async Task<IDialogReference> OpenDialogAsync(
        long? userId = null,
        string? username = null)
    {
        var parameters = new DialogParameters<TempBanDialog>();
        if (userId != null || username != null)
            parameters.Add(x => x.User, new UserIdentity(userId ?? 0, username, null, username));

        return await DialogService.ShowAsync<TempBanDialog>("Temp Ban", parameters);
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
    public void HasMultipleButtons()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Should have Cancel and Temp Ban buttons in dialog actions
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Cancel"));
            Assert.That(provider.Markup, Does.Contain("Temp Ban"));
        });
    }

    #endregion

    #region Username Parameter Tests

    [Test]
    public void DisplaysUsername()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(username: "testuser123");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("testuser123"));
        });
    }

    [Test]
    public void DisplaysBanConfirmationMessage()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync(username: "spammer");

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Temporarily ban"));
            Assert.That(provider.Markup, Does.Contain("spammer"));
            Assert.That(provider.Markup, Does.Contain("from the group"));
        });
    }

    [Test]
    public void DisplaysAutoUnbanMessage()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("automatically unbanned after the selected duration"));
        });
    }

    #endregion

    #region Duration Preset Tests

    [Test]
    public void DisplaysFiveMinutePreset()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("5 Minutes"));
        });
    }

    [Test]
    public void DisplaysOneHourPreset()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("1 Hour"));
        });
    }

    [Test]
    public void DisplaysTwentyFourHourPreset()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("24 Hours"));
        });
    }

    [Test]
    public void DisplaysQuickDurationLabel()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Quick Duration:"));
        });
    }

    #endregion

    #region Custom Duration Tests

    [Test]
    public void HasCustomDurationInput()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Custom Duration (minutes)"));
        });
    }

    [Test]
    public void HasApplyButton()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Apply"));
        });
    }

    #endregion

    #region Reason Input Tests

    [Test]
    public void HasReasonInput()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Reason (optional)"));
        });
    }

    [Test]
    public void HasReasonPlaceholder()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Repeated spam warnings"));
        });
    }

    #endregion

    #region Alert Tests

    [Test]
    public void DisplaysAuditLogWarning()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("recorded in the audit log"));
            Assert.That(provider.Markup, Does.Contain("visible to other admins"));
        });
    }

    [Test]
    public void NoSelectedDurationAlert_WhenNoDurationSelected()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - No info alert for selected duration should be visible
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Not.Contain("Selected duration:"));
            Assert.That(provider.Markup, Does.Not.Contain("Unban time:"));
        });
    }

    #endregion

    #region Button State Tests

    [Test]
    public void TempBanButtonIsDisabled_WhenNoDuration()
    {
        // Arrange
        var provider = RenderDialogProvider();

        // Act
        _ = OpenDialogAsync();

        // Assert - Temp Ban button should be disabled initially
        provider.WaitForAssertion(() =>
        {
            var buttons = provider.FindAll("button");
            var tempBanButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Temp Ban"));
            Assert.That(tempBanButton, Is.Not.Null);
            Assert.That(tempBanButton!.HasAttribute("disabled"), Is.True);
        });
    }

    #endregion

    #region Cancel Button Tests

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

    #region Duration Preset Click Tests

    [Test]
    public void ClickingFiveMinutes_ShowsSelectedDuration()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("5 Minutes"));
        });

        // Act - Click 5 minutes button
        var fiveMinButton = provider.FindAll("button").First(b => b.TextContent.Contains("5 Minutes"));
        fiveMinButton.Click();

        // Assert - Info alert should show selected duration
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Selected duration:"));
            Assert.That(provider.Markup, Does.Contain("5 minutes"));
        });
    }

    [Test]
    public void ClickingOneHour_ShowsSelectedDuration()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("1 Hour"));
        });

        // Act - Click 1 hour button
        var oneHourButton = provider.FindAll("button").First(b => b.TextContent.Contains("1 Hour"));
        oneHourButton.Click();

        // Assert - Info alert should show selected duration
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Selected duration:"));
            Assert.That(provider.Markup, Does.Contain("1 hour"));
        });
    }

    [Test]
    public void ClickingTwentyFourHours_ShowsSelectedDuration()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("24 Hours"));
        });

        // Act - Click 24 hours button
        var twentyFourHourButton = provider.FindAll("button").First(b => b.TextContent.Contains("24 Hours"));
        twentyFourHourButton.Click();

        // Assert - Info alert should show selected duration (24 hours = 1 day)
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Selected duration:"));
            Assert.That(provider.Markup, Does.Contain("1 day"));
        });
    }

    [Test]
    public void SelectingDuration_EnablesTempBanButton()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("1 Hour"));
        });

        // Act - Click 1 hour button
        var oneHourButton = provider.FindAll("button").First(b => b.TextContent.Contains("1 Hour"));
        oneHourButton.Click();

        // Assert - Temp Ban button should be enabled
        provider.WaitForAssertion(() =>
        {
            var tempBanButton = provider.FindAll("button").First(b => b.TextContent.Contains("Temp Ban"));
            Assert.That(tempBanButton.HasAttribute("disabled"), Is.False);
        });
    }

    [Test]
    public void SelectingDuration_UpdatesButtonText()
    {
        // Arrange
        var provider = RenderDialogProvider();
        _ = OpenDialogAsync();

        // Wait for dialog to render
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("5 Minutes"));
        });

        // Act - Click 5 minutes button
        var fiveMinButton = provider.FindAll("button").First(b => b.TextContent.Contains("5 Minutes"));
        fiveMinButton.Click();

        // Assert - Temp Ban button should show duration
        provider.WaitForAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Temp Ban 5 minutes"));
        });
    }

    #endregion

    // Note: Testing custom duration input requires MudNumericField binding,
    // which is complex in bUnit. E2E tests are better for custom duration scenarios.
    // Testing dialog result values requires awaiting dialogRef.Result which causes
    // deadlocks in bUnit.
}
