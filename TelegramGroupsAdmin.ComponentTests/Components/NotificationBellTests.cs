using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using TelegramGroupsAdmin.Components.Shared;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Services;

namespace TelegramGroupsAdmin.ComponentTests.Components;

/// <summary>
/// Component tests for NotificationBell.razor
/// Tests badge visibility, icon colors, and state handling.
/// Uses INotificationStateService interface (enabled by Issue #127 interface extraction).
/// </summary>
/// <remarks>
/// NotificationBell uses MudMenu which renders ChildContent in a popover.
/// These tests focus on the activator content (badge, icon) which is testable via bUnit.
/// The popover content (buttons, notifications list) requires more complex setup to test.
/// </remarks>
[TestFixture]
public class NotificationBellTests : MudBlazorTestContext
{
    private readonly INotificationStateService _mockNotificationState;

    public NotificationBellTests()
    {
        // Create and register mock in constructor (before base constructor locks service provider)
        _mockNotificationState = Substitute.For<INotificationStateService>();

        // Default setup: loaded with no notifications
        _mockNotificationState.IsLoaded.Returns(true);
        _mockNotificationState.UnreadCount.Returns(0);
        _mockNotificationState.Notifications.Returns(Array.Empty<WebNotification>());

        // Register mock service - must be in constructor for bUnit
        Services.AddSingleton(_mockNotificationState);
    }

    #region Badge Visibility Tests

    [Test]
    public void ShowsBadge_WhenUnreadCountGreaterThanZero()
    {
        // Arrange
        _mockNotificationState.UnreadCount.Returns(5);

        // Act
        var cut = Render<NotificationBell>();

        // Assert - badge should be visible with unread count
        Assert.That(cut.Markup, Does.Contain("5"));
    }

    [Test]
    public void HidesBadge_WhenUnreadCountIsZero()
    {
        // Arrange
        _mockNotificationState.UnreadCount.Returns(0);

        // Act
        var cut = Render<NotificationBell>();

        // Assert - badge should not be visible when unread count is 0
        // MudBadge with Visible="false" adds the "mud-badge-dot" class or hides the badge element
        var visibleBadges = cut.FindAll(".mud-badge-visible");
        Assert.That(visibleBadges.Count, Is.EqualTo(0), "Badge should not be visible when unread count is 0");
    }

    [Test]
    public void AppliesWarningColor_WhenUnreadCountGreaterThanZero()
    {
        // Arrange
        _mockNotificationState.UnreadCount.Returns(3);

        // Act
        var cut = Render<NotificationBell>();

        // Assert - icon button should have warning color
        // Using FindComponent instead of CSS classes for stability across MudBlazor versions
        var iconButton = cut.FindComponent<MudIconButton>();
        Assert.That(iconButton.Instance.Color, Is.EqualTo(Color.Warning));
    }

    [Test]
    public void AppliesInheritColor_WhenUnreadCountIsZero()
    {
        // Arrange
        _mockNotificationState.UnreadCount.Returns(0);

        // Act
        var cut = Render<NotificationBell>();

        // Assert - icon button should have inherit color (default)
        // Using FindComponent instead of CSS classes for stability across MudBlazor versions
        var iconButton = cut.FindComponent<MudIconButton>();
        Assert.That(iconButton.Instance.Color, Is.EqualTo(Color.Inherit));
    }

    #endregion

    #region Badge Content Tests

    [Test]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(99)]
    public void DisplaysCorrectUnreadCount(int unreadCount)
    {
        // Arrange
        _mockNotificationState.UnreadCount.Returns(unreadCount);

        // Act
        var cut = Render<NotificationBell>();

        // Assert - badge should display the unread count
        Assert.That(cut.Markup, Does.Contain(unreadCount.ToString()));
    }

    #endregion
}
