using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Messages;

/// <summary>
/// Tests for the Messages page (/messages).
/// Verifies chat list display, message viewing, and permission-based access.
/// </summary>
[TestFixture]
public class MessagesTests : AuthenticatedTestBase
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

        var chat1 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Alpha Chat")
            .AsGroup()
            .BuildAsync();

        var chat2 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Beta Chat")
            .AsSupergroup()
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

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

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Alpha Group")
            .BuildAsync();

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Beta Channel")
            .BuildAsync();

        await new TestChatBuilder(Factory.Services)
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

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Developers Group")
            .BuildAsync();

        await new TestChatBuilder(Factory.Services)
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

        var chat = await new TestChatBuilder(Factory.Services)
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

        var chat = await new TestChatBuilder(Factory.Services)
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

        var chat = await new TestChatBuilder(Factory.Services)
            .WithTitle("Active Chat")
            .BuildAsync();

        // Create test messages
        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(111, "alice", "Alice")
            .WithText("Hello everyone!")
            .At(DateTimeOffset.UtcNow.AddMinutes(-5))
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(222, "bob", "Bob")
            .WithText("Hi Alice!")
            .At(DateTimeOffset.UtcNow.AddMinutes(-3))
            .BuildAsync();

        await new TestMessageBuilder(Factory.Services)
            .InChat(chat)
            .FromUser(111, "alice", "Alice")
            .WithText("How is everyone today?")
            .At(DateTimeOffset.UtcNow.AddMinutes(-1))
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();
        await _messagesPage.SelectChatByNameAsync("Active Chat");

        // Wait for MudVirtualize to render messages (may need more time)
        await Page.WaitForTimeoutAsync(1500);

        // Assert - messages are displayed (using virtualization, may not all be visible)
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

        var chat1 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Public Chat")
            .BuildAsync();

        var chat2 = await new TestChatBuilder(Factory.Services)
            .WithTitle("Private Chat")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

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

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat 1")
            .BuildAsync();

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat 2")
            .BuildAsync();

        await new TestChatBuilder(Factory.Services)
            .WithTitle("Chat 3")
            .BuildAsync();

        // Act
        await _messagesPage.NavigateAsync();
        await _messagesPage.WaitForLoadAsync();

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

        var chat = await new TestChatBuilder(Factory.Services)
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
}
