using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Users;

/// <summary>
/// Tests for the Users page (/users).
/// Verifies Telegram user management display, search, tabs, and permission-based access.
/// Note: This page requires GlobalAdmin or Owner role - Admin cannot access.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class UsersTests : SharedAuthenticatedTestBase
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
        await Expect(_usersPage.PageTitle).ToBeVisibleAsync();
        await Expect(_usersPage.PageTitle).ToHaveTextAsync("Telegram Users");
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
        await Expect(_usersPage.PageTitle).ToBeVisibleAsync();
    }

    [Test]
    public async Task Users_RequiresAuthentication()
    {
        // Act - try to access users without login
        await Page.GotoAsync("/users");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new Regex("/(login|register)"));
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
        await Expect(_usersPage.PageTitle).ToBeVisibleAsync();
        await Expect(_usersPage.PageTitle).ToHaveTextAsync("Telegram Users");
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
        await Expect(_usersPage.Tabs).ToBeVisibleAsync();
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
        // 'All' should be the first tab
        await Expect(_usersPage.TabHeaders.First).ToContainTextAsync(new Regex("ALL"), new() { IgnoreCase = true });
        await Expect(_usersPage.TabHeader("ACTIVE")).ToBeVisibleAsync();
        await Expect(_usersPage.TabHeader("TAGGED")).ToBeVisibleAsync();
        await Expect(_usersPage.TabHeader("TRUSTED")).ToBeVisibleAsync();
        await Expect(_usersPage.TabHeader("BANNED")).ToBeVisibleAsync();
        await Expect(_usersPage.TabHeader("KICKED")).ToBeVisibleAsync();
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
        await _usersPage.ExpectTotalUserCountAsync(0);
    }

    [Test]
    public async Task Users_DisplaysUserList_WhenUsersExist()
    {
        // Arrange - create chat with Telegram users who have sent messages
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Test Chat")
            .BuildAsync();

        // Create Telegram users in telegram_users table
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(111111)
            .WithUsername("alice")
            .WithName("Alice", "Smith")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(222222)
            .WithUsername("bob")
            .WithName("Bob", "Jones")
            .BuildAsync();

        // Create messages from those users (required for chat count stats)
        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(111111, "alice", "Alice", "Smith")
            .WithText("Hello!")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(222222, "bob", "Bob", "Jones")
            .WithText("Hi there!")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - users are displayed
        await _usersPage.ExpectTotalUserCountAtLeastAsync(2);
    }

    [Test]
    public async Task Users_SearchFilters_UserList()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Search Test Chat")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(333333)
            .WithUsername("developer")
            .WithName("Developer", "Dan")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(444444)
            .WithUsername("designer")
            .WithName("Designer", "Diana")
            .BuildAsync();

        // Create messages from those users
        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(333333, "developer", "Developer", "Dan")
            .WithText("Code review")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(444444, "designer", "Designer", "Diana")
            .WithText("UI feedback")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Search for "Developer"
        await _usersPage.SearchUsersAsync("Developer");

        // Wait for the filtered user to appear in the table (server-side search with debounce)
        var developerRow = Page.Locator(".mud-table-body tr:has-text('Developer')");
        await Expect(developerRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        // Should display users matching 'Developer' (case-sensitive, as before)
        await Expect(_usersPage.UserDisplayNames.Filter(new() { HasTextRegex = new Regex("Developer") }).First)
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task Users_ShowsAllUsers_WhenSearchCleared()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Clear Search Chat")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(555555)
            .WithUsername("userA")
            .WithName("User", "Alpha")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(666666)
            .WithUsername("userB")
            .WithName("User", "Beta")
            .BuildAsync();

        // Create messages
        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(555555, "userA", "User", "Alpha")
            .WithText("Message A")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(666666, "userB", "User", "Beta")
            .WithText("Message B")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Sync on the table's data having loaded before reading the baseline total
        await Expect(_usersPage.UserRows.First).ToBeVisibleAsync();
        var initialCount = await _usersPage.GetTotalUserCountAsync();

        // Search and then clear
        await _usersPage.SearchUsersAsync("Alpha");

        // Wait for search results to load
        var alphaRow = Page.Locator(".mud-table-body tr:has-text('Alpha')");
        await Expect(alphaRow).ToBeVisibleAsync(new() { Timeout = 10000 });

        await _usersPage.ClearSearchAsync();

        // Wait for table to reload with all users
        await _usersPage.WaitForLoadAsync();

        // Assert - all users visible again
        await _usersPage.ExpectTotalUserCountAsync(initialCount);
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

        // Assert - Tagged tab displays its user list (may be empty)
        await Expect(_usersPage.UserTables.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Users_CanSwitchToKickedTab()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Switch to Kicked tab
        await _usersPage.SelectTabAsync("Kicked");

        // Assert - Kicked tab displays its user list (may be empty)
        await Expect(_usersPage.UserTables.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Users_DisplaysUserInfo_InTable()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Info Test Chat")
            .BuildAsync();

        // Create Telegram user and message
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(777777)
            .WithUsername("infouser")
            .WithName("Info", "User")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(777777, "infouser", "Info", "User")
            .WithText("Test message")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - the seeded user's row is listed on the default (All) tab
        await Expect(_usersPage.UserRow("Info User")).ToBeVisibleAsync();

        // Switch to the Active tab, which shows per-user Chats and Status columns
        await _usersPage.SelectTabAsync("Active");
        await Expect(_usersPage.UserRow("Info User")).ToBeVisibleAsync();

        // Chats = number of distinct chats the user has messaged in (one seeded message, one chat)
        await Expect(_usersPage.UserChatsCell("Info User")).ToHaveTextAsync(new Regex(@"^\s*1\s*$"));

        // Status = Clean (not trusted, banned, warned or tagged); the chip prefixes a status icon
        await Expect(_usersPage.UserStatusChip("Info User")).ToContainTextAsync("Clean");
    }

    [Test]
    public async Task Users_GlobalAdminSeesAllUsers()
    {
        // Arrange - GlobalAdmin should see all users across all chats
        await LoginAsGlobalAdminAsync();

        var chat1 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat One")
            .BuildAsync();

        var chat2 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat Two")
            .BuildAsync();

        // Create Telegram users
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(888888)
            .WithUsername("user1")
            .WithName("First", "User")
            .BuildAsync();

        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(999999)
            .WithUsername("user2")
            .WithName("Second", "User")
            .BuildAsync();

        // Create messages
        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat1)
            .FromUser(888888, "user1", "First", "User")
            .WithText("In chat 1")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat2)
            .FromUser(999999, "user2", "Second", "User")
            .WithText("In chat 2")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert - GlobalAdmin sees users from all chats
        await _usersPage.ExpectTotalUserCountAtLeastAsync(2);
    }

    [Test]
    public async Task Users_OwnerSeesAllUsers()
    {
        // Arrange - Owner should see all users
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Owner Test Chat")
            .BuildAsync();

        // Create Telegram user and message
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(101010)
            .WithUsername("ownertest")
            .WithName("Owner", "TestUser")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(101010, "ownertest", "Owner", "TestUser")
            .WithText("Owner can see me")
            .BuildAsync();

        // Act
        await _usersPage.NavigateAsync();
        await _usersPage.WaitForLoadAsync();

        // Assert
        await _usersPage.ExpectTotalUserCountAtLeastAsync(1);
    }
}
