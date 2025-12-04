using Bunit;
using Microsoft.AspNetCore.Components;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for NotificationItem.razor
/// Tests icon/color mapping, time formatting, text truncation, and read/unread states.
/// </summary>
[TestFixture]
public class NotificationItemTests : MudBlazorTestContext
{
    /// <summary>
    /// Creates a WebNotification for testing.
    /// </summary>
    private static WebNotification CreateNotification(
        NotificationEventType eventType = NotificationEventType.SpamDetected,
        string subject = "Test Subject",
        string message = "Test message content",
        bool isRead = false,
        DateTimeOffset? createdAt = null)
    {
        return new WebNotification
        {
            Id = 1,
            UserId = "user-123",
            Subject = subject,
            Message = message,
            EventType = eventType,
            IsRead = isRead,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
    }

    #region Display Tests

    [Test]
    public void DisplaysSubject()
    {
        // Arrange
        var notification = CreateNotification(subject: "Spam Detected!");

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("Spam Detected!"));
    }

    [Test]
    public void DisplaysMessage()
    {
        // Arrange
        var notification = CreateNotification(message: "User sent suspicious content");

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("User sent suspicious content"));
    }

    #endregion

    #region Text Truncation Tests

    [Test]
    public void TruncatesLongMessage()
    {
        // Arrange - message over 100 characters
        var longMessage = new string('A', 150);
        var notification = CreateNotification(message: longMessage);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - should be truncated with ellipsis
        Assert.That(cut.Markup, Does.Contain("..."));
        Assert.That(cut.Markup, Does.Not.Contain(longMessage));
    }

    [Test]
    public void DoesNotTruncateShortMessage()
    {
        // Arrange - message under 100 characters
        var shortMessage = "Short message";
        var notification = CreateNotification(message: shortMessage);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - full message shown, no ellipsis
        Assert.That(cut.Markup, Does.Contain(shortMessage));
        Assert.That(cut.Markup, Does.Not.Contain("..."));
    }

    #endregion

    #region Read/Unread State Tests

    [Test]
    public void ShowsMarkAsReadButton_WhenUnread()
    {
        // Arrange
        var notification = CreateNotification(isRead: false);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - mark as read button should exist (check icon)
        var buttons = cut.FindAll("button");
        Assert.That(buttons.Count, Is.EqualTo(2)); // Mark as read + Delete
    }

    [Test]
    public void HidesMarkAsReadButton_WhenRead()
    {
        // Arrange
        var notification = CreateNotification(isRead: true);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - only delete button should exist
        var buttons = cut.FindAll("button");
        Assert.That(buttons.Count, Is.EqualTo(1)); // Only Delete
    }

    [Test]
    public void AppliesUnreadClass_WhenUnread()
    {
        // Arrange
        var notification = CreateNotification(isRead: false);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("notification-unread"));
    }

    [Test]
    public void DoesNotApplyUnreadClass_WhenRead()
    {
        // Arrange
        var notification = CreateNotification(isRead: true);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Not.Contain("notification-unread"));
    }

    #endregion

    #region Time Formatting Tests

    [Test]
    public void DisplaysJustNow_WhenLessThanMinuteAgo()
    {
        // Arrange
        var notification = CreateNotification(createdAt: DateTimeOffset.UtcNow.AddSeconds(-30));

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("just now"));
    }

    [Test]
    public void DisplaysMinutesAgo_WhenLessThanHour()
    {
        // Arrange
        var notification = CreateNotification(createdAt: DateTimeOffset.UtcNow.AddMinutes(-15));

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("15m ago"));
    }

    [Test]
    public void DisplaysHoursAgo_WhenLessThanDay()
    {
        // Arrange
        var notification = CreateNotification(createdAt: DateTimeOffset.UtcNow.AddHours(-5));

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("5h ago"));
    }

    [Test]
    public void DisplaysDaysAgo_WhenLessThanWeek()
    {
        // Arrange
        var notification = CreateNotification(createdAt: DateTimeOffset.UtcNow.AddDays(-3));

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert
        Assert.That(cut.Markup, Does.Contain("3d ago"));
    }

    [Test]
    public void DisplaysDate_WhenOverWeekOld()
    {
        // Arrange - use a specific date so we know what to expect
        var oldDate = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var notification = CreateNotification(createdAt: oldDate);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - should show "Jun 15" format
        Assert.That(cut.Markup, Does.Contain("Jun 15"));
    }

    #endregion

    #region Icon Color Tests (based on EventType)

    [Test]
    [TestCase(NotificationEventType.SpamDetected)]
    [TestCase(NotificationEventType.SpamAutoDeleted)]
    [TestCase(NotificationEventType.ChatHealthWarning)]
    public void AppliesWarningColor_ForWarningEventTypes(NotificationEventType eventType)
    {
        // Arrange
        var notification = CreateNotification(eventType: eventType);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - MudBlazor warning color class
        Assert.That(cut.Markup, Does.Contain("mud-warning-text"));
    }

    [Test]
    [TestCase(NotificationEventType.UserBanned)]
    [TestCase(NotificationEventType.MalwareDetected)]
    [TestCase(NotificationEventType.BackupFailed)]
    public void AppliesErrorColor_ForErrorEventTypes(NotificationEventType eventType)
    {
        // Arrange
        var notification = CreateNotification(eventType: eventType);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - MudBlazor error color class
        Assert.That(cut.Markup, Does.Contain("mud-error-text"));
    }

    [Test]
    [TestCase(NotificationEventType.MessageReported)]
    [TestCase(NotificationEventType.ChatAdminChanged)]
    public void AppliesDefaultColor_ForDefaultEventTypes(NotificationEventType eventType)
    {
        // Arrange
        var notification = CreateNotification(eventType: eventType);

        // Act
        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification));

        // Assert - should not have warning or error class for default items
        Assert.That(cut.Markup, Does.Not.Contain("mud-warning-text"));
        Assert.That(cut.Markup, Does.Not.Contain("mud-error-text"));
    }

    #endregion

    #region Event Callback Tests

    [Test]
    public async Task InvokesOnMarkRead_WhenMarkReadButtonClicked()
    {
        // Arrange
        var callbackInvoked = false;
        var notification = CreateNotification(isRead: false);

        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification)
            .Add(x => x.OnMarkRead, EventCallback.Factory.Create(this, () => callbackInvoked = true)));

        // Act - find and click the first button (mark as read)
        var buttons = cut.FindAll("button");
        await buttons[0].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(callbackInvoked, Is.True);
    }

    [Test]
    public async Task InvokesOnDelete_WhenDeleteButtonClicked()
    {
        // Arrange
        var callbackInvoked = false;
        var notification = CreateNotification(isRead: false);

        var cut = Render<NotificationItem>(p => p
            .Add(x => x.Notification, notification)
            .Add(x => x.OnDelete, EventCallback.Factory.Create(this, () => callbackInvoked = true)));

        // Act - find and click the last button (delete)
        var buttons = cut.FindAll("button");
        await buttons[^1].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        // Assert
        Assert.That(callbackInvoked, Is.True);
    }

    #endregion
}
