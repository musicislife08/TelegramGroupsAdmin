using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Messages;

/// <summary>
/// Tests for the Messages page (/messages).
/// Verifies chat list display, message viewing, and permission-based access.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class MessagesTests : SharedAuthenticatedTestBase
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
        await Expect(_messagesPage.Layout).ToBeVisibleAsync();
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
        await Expect(_messagesPage.Sidebar).ToBeVisibleAsync();
        await Expect(_messagesPage.SidebarTitle).ToHaveTextAsync("Chats");
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
        await Expect(_messagesPage.EmptyState).ToBeVisibleAsync();
        await Expect(_messagesPage.EmptyStateText).ToContainTextAsync("Select a chat");
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
        await Expect(_messagesPage.NoChatsSidebar).ToBeVisibleAsync();

        // Absence check follows the positive empty-state check above, so the sidebar has rendered
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(0);
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

        // Assert - chats are displayed in sidebar
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(2);
        await Expect(_messagesPage.ChatTitleNamed("Alpha Chat")).ToBeVisibleAsync();
        await Expect(_messagesPage.ChatTitleNamed("Beta Chat")).ToBeVisibleAsync();
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
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(2);
        await Expect(_messagesPage.ChatTitleNamed("Alpha Group")).ToBeVisibleAsync();
        await Expect(_messagesPage.ChatTitleNamed("Alpha Team")).ToBeVisibleAsync();

        // Absence check follows the filtered count above, so the filter has already been applied
        await Expect(_messagesPage.ChatTitleNamed("Beta Channel")).ToHaveCountAsync(0);
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
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(1);

        await _messagesPage.ClearChatSearchAsync();

        // Assert - all chats visible again
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(2);
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

        // Wait for Blazor re-render to apply .active class
        await _messagesPage.WaitForChatViewActiveAsync();

        // Assert - chat view becomes active and shows the messages container
        await Expect(_messagesPage.ActiveChatView).ToBeVisibleAsync();
        await Expect(_messagesPage.MessagesContainer).ToBeVisibleAsync();
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

        // Assert - "no messages" state visible
        await Expect(_messagesPage.NoMessagesState).ToBeVisibleAsync();

        // Absence check follows the positive "no messages" check above, so the chat has rendered
        await Expect(_messagesPage.MessageBubbles).ToHaveCountAsync(0);
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

        // Assert - at least one message is displayed (auto-retrying until messages render)
        await Expect(_messagesPage.MessageBubbles.First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Messages_RequiresAuthentication()
    {
        // Act - try to access messages without login
        await Page.GotoAsync("/messages");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new Regex("/(login|register)"));
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
        await Expect(_messagesPage.Layout).ToBeVisibleAsync();
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
        await Expect(_messagesPage.Layout).ToBeVisibleAsync();
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

        // Assert - GlobalAdmin sees all chats
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(2);
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

        // Assert - Owner sees all chats
        await Expect(_messagesPage.ChatItems).ToHaveCountAsync(3);
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

        // Assert - chat is auto-selected
        await Expect(_messagesPage.ActiveChatView).ToBeVisibleAsync();
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
        await Expect(_messagesPage.UserDetailDialog).ToBeVisibleAsync();
        await Expect(_messagesPage.UserDetailDialogTitle).ToContainTextAsync("User Details");
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
        await Expect(_messagesPage.UserDetailDialog).ToContainTextAsync("SpecificUser", new() { Timeout = 5000 });
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
        await Expect(_messagesPage.UserDetailDialog).ToBeVisibleAsync();

        // Press Escape to close
        await _messagesPage.CloseUserDetailDialogByEscapeAsync();
        await _messagesPage.WaitForUserDetailDialogHiddenAsync();

        // Assert - dialog closed
        await Expect(_messagesPage.UserDetailDialog).Not.ToBeVisibleAsync();
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
        await Expect(_messagesPage.UserDetailDialog).ToBeVisibleAsync();

        // Click close button
        await _messagesPage.CloseUserDetailDialogByButtonAsync();
        await _messagesPage.WaitForUserDetailDialogHiddenAsync();

        // Assert - dialog closed
        await Expect(_messagesPage.UserDetailDialog).Not.ToBeVisibleAsync();
    }

    #endregion
}
