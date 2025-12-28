using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// WASM Tests for the Messages page (/messages).
/// Verifies chat list display, message viewing, and permission-based access.
/// Uses WasmSharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmMessagesTests : WasmSharedAuthenticatedTestBase
{
    private MessagesPage _messagesPage = null!;

    [SetUp]
    public void SetUp()
    {
        _messagesPage = new MessagesPage(Page);
    }

    [Test]
    public async Task Messages_LoadsSuccessfully_WhenAuthenticated()
    {
        // Arrange - login as owner
        await LoginAsOwnerAsync();

        // Act - navigate to messages page
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - page layout is visible
        Assert.That(await _messagesPage.IsLayoutVisibleAsync(), Is.True,
            "Messages page layout should be visible");
    }

    [Test]
    public async Task Messages_ShowsSidebar_WhenAuthenticated()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - sidebar is visible with correct title
        Assert.That(await _messagesPage.IsSidebarVisibleAsync(), Is.True,
            "Chat sidebar should be visible");

        var sidebarTitle = await _messagesPage.GetSidebarTitleAsync();
        Assert.That(sidebarTitle, Is.EqualTo("Chats"),
            "Sidebar title should be 'Chats'");
    }

    [Test]
    public async Task Messages_ShowsEmptyState_WhenNoChatSelected()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - empty state shows "Select a chat" message
        Assert.That(await _messagesPage.IsEmptyStateVisibleAsync(), Is.True,
            "Empty state should be visible when no chat is selected");

        var emptyText = await _messagesPage.GetEmptyStateTextAsync();
        Assert.That(emptyText, Does.Contain("Select a chat"),
            "Empty state should prompt user to select a chat");
    }

    [Test]
    public async Task Messages_ShowsNoChatsSidebar_WhenNoChatsExist()
    {
        // Arrange - fresh database, no chats
        await LoginAsOwnerAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - "no chats available" message in sidebar
        Assert.That(await _messagesPage.IsNoChatsSidebarVisibleAsync(), Is.True,
            "Should show 'no chats available' when database has no chats");

        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(0),
            "Chat count should be 0 when no chats exist");
    }

    [Test]
    public async Task Messages_DisplaysChatList_WhenChatsExist()
    {
        // Arrange - create test chats
        await LoginAsOwnerAsync();

        var chat1 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Alpha Chat")
            .AsGroup()
            .BuildAsync();

        var chat2 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Beta Chat")
            .AsSupergroup()
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Wait for chats to load (WASM loads data asynchronously)
        await _messagesPage.WaitForChatsAsync(2);

        // Assert - chats are displayed in sidebar
        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "Should display 2 chats in sidebar");

        var chatNames = await _messagesPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("Alpha Chat"),
            "Should display 'Alpha Chat'");
        Assert.That(chatNames, Does.Contain("Beta Chat"),
            "Should display 'Beta Chat'");
    }

    [Test]
    public async Task Messages_FiltersChatList_WhenSearching()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Alpha Group")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Beta Channel")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Alpha Team")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Search for "Alpha"
        await _messagesPage.SearchChatsAsync("Alpha");

        // Assert - only Alpha chats should be visible
        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "Should only show 2 chats matching 'Alpha'");

        var chatNames = await _messagesPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("Alpha Group"));
        Assert.That(chatNames, Does.Contain("Alpha Team"));
        Assert.That(chatNames, Does.Not.Contain("Beta Channel"));
    }

    [Test]
    public async Task Messages_ShowsAllChats_WhenSearchCleared()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Developers Group")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Marketing Team")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Search to filter, then clear
        await _messagesPage.SearchChatsAsync("Developers");
        var filteredCount = await _messagesPage.GetChatCountAsync();
        Assert.That(filteredCount, Is.EqualTo(1), "Should show 1 chat when filtering for 'Developers'");

        await _messagesPage.ClearChatSearchAsync();

        // Assert - all chats visible again
        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "Should show all 2 chats when search is cleared");
    }

    [Test]
    public async Task Messages_SelectsChat_WhenChatClicked()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Test Group")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        await _messagesPage.SelectChatByNameAsync("Test Group");

        // Assert - chat view becomes active
        Assert.That(await _messagesPage.IsChatViewActiveAsync(), Is.True,
            "Chat view should be active after selecting a chat");

        Assert.That(await _messagesPage.IsMessagesContainerVisibleAsync(), Is.True,
            "Messages container should be visible");
    }

    [Test]
    public async Task Messages_ShowsNoMessagesState_WhenChatHasNoMessages()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Empty Chat")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Empty Chat");

        // Wait for loading to complete
        await Page.WaitForTimeoutAsync(500);

        // Assert - "no messages" state visible
        Assert.That(await _messagesPage.IsNoMessagesStateVisibleAsync(), Is.True,
            "Should show 'no messages' empty state for chat without messages");

        var messageCount = await _messagesPage.GetMessageCountAsync();
        Assert.That(messageCount, Is.EqualTo(0),
            "Message count should be 0 for empty chat");
    }

    [Test]
    public async Task Messages_DisplaysMessages_WhenChatHasMessages()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Active Chat")
            .BuildAsync();

        // Create test messages
        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(111, "alice", "Alice")
            .WithText("Hello everyone!")
            .At(DateTimeOffset.UtcNow.AddMinutes(-5))
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(222, "bob", "Bob")
            .WithText("Hi Alice!")
            .At(DateTimeOffset.UtcNow.AddMinutes(-3))
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(111, "alice", "Alice")
            .WithText("How is everyone today?")
            .At(DateTimeOffset.UtcNow.AddMinutes(-1))
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Active Chat");

        // Assert - wait for messages to appear using Playwright's auto-retry assertions
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();

        var messageCount = await _messagesPage.GetMessageCountAsync();
        Assert.That(messageCount, Is.GreaterThan(0),
            "Should display messages for chat with messages");
    }

    [Test]
    public async Task Messages_RequiresAuthentication()
    {
        // Act - try to access messages without login
        await Page.GotoAsync("/messages");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/(login|register)"));
    }

    [Test]
    public async Task Messages_AccessibleByAdmin()
    {
        // Arrange - login as Admin (lowest permission)
        await LoginAsAdminAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - Admin can view messages page
        Assert.That(await _messagesPage.IsLayoutVisibleAsync(), Is.True,
            "Admin should be able to view messages page");
    }

    [Test]
    public async Task Messages_AccessibleByGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Assert - GlobalAdmin can view messages page
        Assert.That(await _messagesPage.IsLayoutVisibleAsync(), Is.True,
            "GlobalAdmin should be able to view messages page");
    }

    [Test]
    public async Task Messages_GlobalAdminSeesAllChats()
    {
        // Arrange - GlobalAdmin should see all chats (no chat_admins link needed)
        await LoginAsGlobalAdminAsync();

        var chat1 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Public Chat")
            .BuildAsync();

        var chat2 = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Private Chat")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Wait for chats to load (WASM loads data asynchronously)
        await _messagesPage.WaitForChatsAsync(2);

        // Assert - GlobalAdmin sees all chats
        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "GlobalAdmin should see all chats");
    }

    [Test]
    public async Task Messages_OwnerSeesAllChats()
    {
        // Arrange - Owner should see all chats
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat 1")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat 2")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat 3")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

        // Wait for async chat data to load in WASM
        await _messagesPage.WaitForChatsAsync(3);

        // Assert - Owner sees all chats
        var chatCount = await _messagesPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(3),
            "Owner should see all chats");
    }

    [Test]
    public async Task Messages_NavigatesViaQueryString()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Target Chat")
            .BuildAsync();

        // Act - navigate with chat ID in query string
        await _messagesPage.NavigateAsync(chatId: chat.ChatId);
        await _messagesPage.WaitForLoadAsync();

        // Wait for chat to be selected
        await Page.WaitForTimeoutAsync(500);

        // Assert - chat is auto-selected
        Assert.That(await _messagesPage.IsChatViewActiveAsync(), Is.True,
            "Chat should be auto-selected when chatId is in query string");
    }

    #region User Detail Dialog Tests (#107)

    [Test]
    public async Task Messages_OpensUserDetailDialog_WhenUsernameClicked()
    {
        // Arrange - create chat with message
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Test Chat")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(123456789, "testuser", "TestUser")
            .WithText("Hello world!")
            .BuildAsync();

        // Act - navigate, select chat, click username
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Test Chat");
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();

        await _messagesPage.ClickUsernameInMessageAsync();
        await _messagesPage.WaitForUserDetailDialogAsync();

        // Assert - dialog opens with correct title
        Assert.That(await _messagesPage.IsUserDetailDialogVisibleAsync(), Is.True,
            "User detail dialog should be visible after clicking username");

        var dialogTitle = await _messagesPage.GetUserDetailDialogTitleAsync();
        Assert.That(dialogTitle, Does.Contain("User Details"),
            "Dialog should have 'User Details' title");
    }

    [Test]
    public async Task Messages_UserDetailDialog_ShowsCorrectUser()
    {
        // Arrange - create chat with message from specific user
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Dialog User Test")
            .BuildAsync();

        // Create the telegram_users record (required for UserDetailDialog lookup)
        await new TestTelegramUserBuilder(SharedFactory.Services)
            .WithUserId(987654321)
            .WithUsername("specificuser")
            .WithName("SpecificUser", "Smith")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(987654321, "specificuser", "SpecificUser", "Smith")
            .WithText("Test message from specific user")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Dialog User Test");
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();

        await _messagesPage.ClickUsernameInMessageAsync();
        await _messagesPage.WaitForUserDetailDialogAsync();

        // Assert - dialog shows user info (wait for async content load)
        // Use Playwright's Expect with auto-retry for async content
        var dialog = Page.GetByRole(Microsoft.Playwright.AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync("SpecificUser", new() { Timeout = 5000 });
    }

    [Test]
    public async Task Messages_UserDetailDialog_ClosesOnEscapeKey()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Escape Test Chat")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(111, "escapeuser", "EscapeUser")
            .WithText("Test escape close")
            .BuildAsync();

        // Act - open dialog
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Escape Test Chat");
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();

        await _messagesPage.ClickUsernameInMessageAsync();
        await _messagesPage.WaitForUserDetailDialogAsync();
        Assert.That(await _messagesPage.IsUserDetailDialogVisibleAsync(), Is.True,
            "Dialog should be open before pressing Escape");

        // Press Escape to close
        await _messagesPage.CloseUserDetailDialogByEscapeAsync();
        await _messagesPage.WaitForUserDetailDialogHiddenAsync();

        // Assert - dialog closed
        Assert.That(await _messagesPage.IsUserDetailDialogVisibleAsync(), Is.False,
            "Dialog should close when Escape is pressed");
    }

    [Test]
    public async Task Messages_UserDetailDialog_ClosesOnCloseButton()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Close Button Test")
            .BuildAsync();

        await new TestMessageBuilder(SharedFactory.Services)
            .InChat(chat)
            .FromUser(222, "closeuser", "CloseUser")
            .WithText("Test close button")
            .BuildAsync();

        // Act - open dialog
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Close Button Test");
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();

        await _messagesPage.ClickUsernameInMessageAsync();
        await _messagesPage.WaitForUserDetailDialogAsync();
        Assert.That(await _messagesPage.IsUserDetailDialogVisibleAsync(), Is.True,
            "Dialog should be open before clicking close button");

        // Click close button
        await _messagesPage.CloseUserDetailDialogByButtonAsync();
        await _messagesPage.WaitForUserDetailDialogHiddenAsync();

        // Assert - dialog closed
        Assert.That(await _messagesPage.IsUserDetailDialogVisibleAsync(), Is.False,
            "Dialog should close when close button is clicked");
    }

    #endregion
}
