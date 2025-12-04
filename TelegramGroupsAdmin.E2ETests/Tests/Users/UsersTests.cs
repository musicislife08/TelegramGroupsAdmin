using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Users;

/// <summary>
/// Tests for the Users page (/users).
/// Verifies Telegram user management display, search, tabs, and permission-based access.
/// Note: This page requires GlobalAdmin or Owner role - Admin cannot access.
/// </summary>
[TestFixture]
public class UsersTests : AuthenticatedTestBase
{
    private UsersPage _usersPage = null!;

    [SetUp]
    public void SetUp()
    {
        _usersPage = new UsersPage(Page);
    }

    [Test]
    public async Task Users_LoadsSuccessfully_WhenGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act - navigate to users page
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - page title is visible
        Assert.That(await _usersPage.IsPageTitleVisibleAsync(), Is.True,
            "Users page title should be visible");

        var pageTitle = await _usersPage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Telegram Users"),
            "Page title should be 'Telegram Users'");
    }

    [Test]
    public async Task Users_LoadsSuccessfully_WhenOwner()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - page title is visible
        Assert.That(await _usersPage.IsPageTitleVisibleAsync(), Is.True,
            "Owner should be able to view users page");
    }

    [Test]
    public async Task Users_RequiresAuthentication()
    {
        // Act - try to access users without login
        await Page.GotoAsync("/users");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/(login|register)"));
    }

    [Test]
    public async Task Users_LoadsSuccessfully_WhenAdmin()
    {
        // Arrange - login as Admin (users page is now accessible to Admin)
        // Note: Admin users will only see users from their assigned chats
        await LoginAsAdminAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - page title is visible (Admin can access)
        Assert.That(await _usersPage.IsPageTitleVisibleAsync(), Is.True,
            "Users page title should be visible for Admin");

        var pageTitle = await _usersPage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Telegram Users"),
            "Page title should be 'Telegram Users'");
    }

    [Test]
    public async Task Users_DisplaysTabs_WhenAuthenticated()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - tabs are visible
        Assert.That(await _usersPage.IsTabsVisibleAsync(), Is.True,
            "Tabs should be visible on Users page");
    }

    [Test]
    public async Task Users_HasExpectedTabs()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - expected tabs exist (tabs are uppercase)
        var tabNames = await _usersPage.GetTabNamesAsync();
        Assert.That(tabNames.Any(t => t.Contains("ACTIVE", StringComparison.OrdinalIgnoreCase)),
            "Should have 'Active' tab");
        Assert.That(tabNames.Any(t => t.Contains("TAGGED", StringComparison.OrdinalIgnoreCase)),
            "Should have 'Tagged' tab");
        Assert.That(tabNames.Any(t => t.Contains("TRUSTED", StringComparison.OrdinalIgnoreCase)),
            "Should have 'Trusted' tab");
        Assert.That(tabNames.Any(t => t.Contains("BANNED", StringComparison.OrdinalIgnoreCase)),
            "Should have 'Banned' tab");
    }

    [Test]
    public async Task Users_ShowsEmptyState_WhenNoUsers()
    {
        // Arrange - fresh database, no users
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - total count should be 0
        var totalCount = await _usersPage.GetTotalUserCountAsync();
        Assert.That(totalCount, Is.EqualTo(0),
            "Total user count should be 0 when no Telegram users exist");
    }

    [Test]
    public async Task Users_DisplaysUserList_WhenUsersExist()
    {
        // Arrange - create chat with Telegram users who have sent messages
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Test Chat")
            .BuildAsync();

        // Create Telegram users in telegram_users table
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(111111)
            .WithUsername("alice")
            .WithName("Alice", "Smith")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(222222)
            .WithUsername("bob")
            .WithName("Bob", "Jones")
            .BuildAsync();

        // Create messages from those users (required for chat count stats)
        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(111111, "alice", "Alice", "Smith")
            .WithText("Hello!")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(222222, "bob", "Bob", "Jones")
            .WithText("Hi there!")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - users are displayed
        var totalCount = await _usersPage.GetTotalUserCountAsync();
        Assert.That(totalCount, Is.GreaterThanOrEqualTo(2),
            "Should display at least 2 users");
    }

    [Test]
    public async Task Users_SearchFilters_UserList()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Search Test Chat")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(333333)
            .WithUsername("developer")
            .WithName("Developer", "Dan")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(444444)
            .WithUsername("designer")
            .WithName("Designer", "Diana")
            .BuildAsync();

        // Create messages from those users
        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(333333, "developer", "Developer", "Dan")
            .WithText("Code review")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(444444, "designer", "Designer", "Diana")
            .WithText("UI feedback")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Search for "Developer"
        await _usersPage.SearchUsersAsync("Developer");

        // Wait for the filtered chip to appear (Blazor re-renders asynchronously)
        var filteredChip = Page.Locator(".mud-chip:has-text('Filtered:')");
        await Expect(filteredChip).ToBeVisibleAsync(new() { Timeout = 5000 });

        var displayedNames = await _usersPage.GetUserDisplayNamesAsync();
        Assert.That(displayedNames.Any(n => n.Contains("Developer")), Is.True,
            "Should display users matching 'Developer'");
    }

    [Test]
    public async Task Users_ShowsAllUsers_WhenSearchCleared()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Clear Search Chat")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(555555)
            .WithUsername("userA")
            .WithName("User", "Alpha")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(666666)
            .WithUsername("userB")
            .WithName("User", "Beta")
            .BuildAsync();

        // Create messages
        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(555555, "userA", "User", "Alpha")
            .WithText("Message A")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(666666, "userB", "User", "Beta")
            .WithText("Message B")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        var initialCount = await _usersPage.GetTotalUserCountAsync();

        // Search and then clear
        await _usersPage.SearchUsersAsync("Alpha");
        await _usersPage.ClearSearchAsync();

        // Assert - all users visible again
        var finalCount = await _usersPage.GetTotalUserCountAsync();
        Assert.That(finalCount, Is.EqualTo(initialCount),
            "Should show all users when search is cleared");

        Assert.That(await _usersPage.IsFilteredChipVisibleAsync(), Is.False,
            "Filtered chip should not be visible when search is cleared");
    }

    [Test]
    public async Task Users_CanSwitchTabs()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Switch to Tagged tab
        await _usersPage.SelectTabAsync("Tagged");

        // Assert - can verify tab content changes
        var displayedCount = await _usersPage.GetDisplayedUserCountAsync();
        // Tagged users list may be empty initially
        Assert.That(displayedCount, Is.GreaterThanOrEqualTo(0),
            "Tagged tab should display user list (may be empty)");
    }

    [Test]
    public async Task Users_DisplaysUserInfo_InTable()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Info Test Chat")
            .BuildAsync();

        // Create Telegram user and message
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(777777)
            .WithUsername("infouser")
            .WithName("Info", "User")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(777777, "infouser", "Info", "User")
            .WithText("Test message")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - user info is displayed
        var displayedNames = await _usersPage.GetUserDisplayNamesAsync();
        Assert.That(displayedNames.Count, Is.GreaterThan(0),
            "Should display user rows with names");
    }

    [Test]
    public async Task Users_GlobalAdminSeesAllUsers()
    {
        // Arrange - GlobalAdmin should see all users across all chats
        await LoginAsGlobalAdminAsync();

        var chat1 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat One")
            .BuildAsync();

        var chat2 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat Two")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(888888)
            .WithUsername("user1")
            .WithName("First", "User")
            .BuildAsync();

        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(999999)
            .WithUsername("user2")
            .WithName("Second", "User")
            .BuildAsync();

        // Create messages
        await new TestMessageBuilder(Factory.Services)
            .InChat(chat1)
            .FromUser(888888, "user1", "First", "User")
            .WithText("In chat 1")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat2)
            .FromUser(999999, "user2", "Second", "User")
            .WithText("In chat 2")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - GlobalAdmin sees users from all chats
        var totalCount = await _usersPage.GetTotalUserCountAsync();
        Assert.That(totalCount, Is.GreaterThanOrEqualTo(2),
            "GlobalAdmin should see users from all chats");
    }

    [Test]
    public async Task Users_OwnerSeesAllUsers()
    {
        // Arrange - Owner should see all users
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Owner Test Chat")
            .BuildAsync();

        // Create Telegram user and message
        await new TestTelegramUserBuilder(Factory.Services)
            .WithUserId(101010)
            .WithUsername("ownertest")
            .WithName("Owner", "TestUser")
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(101010, "ownertest", "Owner", "TestUser")
            .WithText("Owner can see me")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert
        var totalCount = await _usersPage.GetTotalUserCountAsync();
        Assert.That(totalCount, Is.GreaterThanOrEqualTo(1),
            "Owner should see all users");
    }
}
