using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Components;

/// <summary>
/// E2E tests for the NotificationBell component.
/// Tests dropdown interactions that can't be tested via bUnit due to MudMenu popover limitations.
/// </summary>
/// <remarks>
/// The NotificationBell uses MudMenu which renders its ChildContent in a popover.
/// bUnit can only test the activator content (badge, icon). These E2E tests cover
/// the full dropdown interaction experience.
/// </remarks>
[TestFixture]
public class NotificationBellTests : SharedAuthenticatedTestBase
{
    /// <summary>
    /// Locator for the notification bell button in the app bar.
    /// </summary>
    private ILocator NotificationBellButton => Page.Locator(".notification-bell button");

    /// <summary>
    /// Locator for the notification badge showing unread count.
    /// MudBlazor renders the badge content in a span with class "mud-badge" (not "mud-badge-content").
    /// </summary>
    private ILocator NotificationBadge => Page.Locator(".notification-bell .mud-badge");

    /// <summary>
    /// Locator for the dropdown menu content (visible after clicking bell).
    /// </summary>
    private ILocator DropdownMenu => Page.Locator(".mud-popover-open");

    /// <summary>
    /// Locator for the "Mark all read" button in the dropdown.
    /// </summary>
    private ILocator MarkAllReadButton => DropdownMenu.GetByRole(AriaRole.Button, new() { Name = "Mark all read" });

    /// <summary>
    /// Locator for the "Clear all" button in the dropdown.
    /// </summary>
    private ILocator ClearAllButton => DropdownMenu.GetByRole(AriaRole.Button, new() { Name = "Clear all" });

    /// <summary>
    /// Locator for the empty state message.
    /// </summary>
    private ILocator EmptyStateMessage => DropdownMenu.GetByText("No notifications yet");

    [Test]
    public async Task NotificationBell_WithNoNotifications_ShowsEmptyState()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await NavigateToAsync("/");

        // Act - click the notification bell
        await NotificationBellButton.ClickAsync();

        // Assert - dropdown shows empty state
        await Expect(DropdownMenu).ToBeVisibleAsync();
        await Expect(EmptyStateMessage).ToBeVisibleAsync();
        await Expect(MarkAllReadButton).Not.ToBeVisibleAsync();
        await Expect(ClearAllButton).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationBell_WithUnreadNotifications_ShowsBadgeCount()
    {
        // Arrange - create user and notifications BEFORE login
        // This ensures notifications exist when NotificationStateService initializes
        var user = await CreateUserAsync(PermissionLevel.Owner);

        // Create 3 unread notifications before the first navigation
        for (var i = 1; i <= 3; i++)
        {
            await new TestWebNotificationBuilder(SharedFactory.Services)
                .ForUser(user.Id)
                .WithSubject($"Test Notification {i}")
                .WithMessage($"This is test notification {i}")
                .AsUnread()
                .BuildAsync();
        }

        // Now log in (injects cookie) and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");

        // Wait for Blazor Server's async initialization to complete
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - badge shows count of 3
        await Expect(NotificationBadge).ToBeVisibleAsync();
        await Expect(NotificationBadge).ToHaveTextAsync("3");
    }

    [Test]
    public async Task NotificationBell_ClickingBell_OpensDropdownWithNotifications()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsSpamDetected("Test Chat", "spammer123")
            .AsUnread()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Act - click the notification bell
        await NotificationBellButton.ClickAsync();

        // Assert - dropdown shows notification content
        await Expect(DropdownMenu).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("Spam Detected")).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("spammer123")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationBell_MarkAllRead_ClearsBadgeAndHidesButton()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        // Create 2 unread notifications before the first navigation
        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsSpamDetected()
            .AsUnread()
            .BuildAsync();

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsMessageReported()
            .AsUnread()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Verify badge shows 2 unread
        await Expect(NotificationBadge).ToBeVisibleAsync();
        await Expect(NotificationBadge).ToHaveTextAsync("2");

        // Act - open dropdown and click "Mark all read"
        await NotificationBellButton.ClickAsync();
        await Expect(MarkAllReadButton).ToBeVisibleAsync();
        await MarkAllReadButton.ClickAsync();

        // Assert - badge disappears, "Mark all read" button hidden
        await Expect(NotificationBadge).Not.ToBeVisibleAsync();

        // Re-open dropdown to verify button is hidden
        await NotificationBellButton.ClickAsync();
        await Expect(MarkAllReadButton).Not.ToBeVisibleAsync();
        // But "Clear all" should still be visible (notifications exist, just read)
        await Expect(ClearAllButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationBell_ClearAll_RemovesAllNotifications()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsSpamDetected()
            .AsUnread()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Open dropdown
        await NotificationBellButton.ClickAsync();
        await Expect(DropdownMenu.GetByText("Spam Detected")).ToBeVisibleAsync();

        // Act - click "Clear all"
        await ClearAllButton.ClickAsync();

        // Assert - dropdown now shows empty state
        // Need to re-open the dropdown as it may close after action
        await NotificationBellButton.ClickAsync();
        await Expect(EmptyStateMessage).ToBeVisibleAsync();
        await Expect(NotificationBadge).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationBell_MultipleEventTypes_DisplayCorrectly()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsSpamDetected("Chat A", "user1")
            .AsUnread()
            .BuildAsync();

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsUserBanned("banned_guy", 5)
            .AsUnread()
            .BuildAsync();

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .AsMalwareDetected("virus.exe")
            .AsUnread()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Act - open dropdown
        await NotificationBellButton.ClickAsync();

        // Assert - all notification types display
        await Expect(DropdownMenu.GetByText("Spam Detected")).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("User Banned")).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("Malware Detected")).ToBeVisibleAsync();

        // Badge shows total count
        await Expect(NotificationBadge).ToBeVisibleAsync();
        await Expect(NotificationBadge).ToHaveTextAsync("3");
    }

    [Test]
    public async Task NotificationBell_ReadNotifications_NotCountedInBadge()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        // Create 2 unread and 1 read notification before the first navigation
        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .WithSubject("Unread 1")
            .AsUnread()
            .BuildAsync();

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .WithSubject("Unread 2")
            .AsUnread()
            .BuildAsync();

        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .WithSubject("Already Read")
            .AsRead()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - badge only shows unread count (2, not 3)
        await Expect(NotificationBadge).ToBeVisibleAsync();
        await Expect(NotificationBadge).ToHaveTextAsync("2");

        // Open dropdown - all 3 notifications should be visible
        await NotificationBellButton.ClickAsync();
        await Expect(DropdownMenu.GetByText("Unread 1")).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("Unread 2")).ToBeVisibleAsync();
        await Expect(DropdownMenu.GetByText("Already Read")).ToBeVisibleAsync();
    }

    [Test]
    public async Task NotificationBell_NoBadge_WhenAllNotificationsRead()
    {
        // Arrange - create user and notifications BEFORE login
        var user = await CreateUserAsync(PermissionLevel.Owner);

        // Create only read notifications before the first navigation
        await new TestWebNotificationBuilder(SharedFactory.Services)
            .ForUser(user.Id)
            .WithSubject("Read Notification")
            .AsRead()
            .BuildAsync();

        // Now log in and navigate
        await LoginAsAsync(user);
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - no badge visible (all read)
        await Expect(NotificationBadge).Not.ToBeVisibleAsync();

        // But dropdown should still show the notification
        await NotificationBellButton.ClickAsync();
        await Expect(DropdownMenu.GetByText("Read Notification")).ToBeVisibleAsync();
        await Expect(EmptyStateMessage).Not.ToBeVisibleAsync();
    }
}
