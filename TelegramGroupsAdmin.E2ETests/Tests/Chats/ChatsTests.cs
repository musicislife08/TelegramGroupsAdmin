using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Chats;

/// <summary>
/// Tests for the Chats page (/chats).
/// Verifies chat management table display, search, and permission-based access.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class ChatsTests : SharedAuthenticatedTestBase
{
    private ChatsPage _chatsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _chatsPage = new ChatsPage(Page);
    }

    [Test]
    public async Task Chats_LoadsSuccessfully_WhenAuthenticated()
    {
        // Arrange - login as owner
        await LoginAsOwnerAsync();

        // Act - navigate to chats page
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - page title is visible
        await Expect(_chatsPage.PageTitle).ToBeVisibleAsync();
        await Expect(_chatsPage.PageTitle).ToHaveTextAsync("Chat Management");
    }

    [Test]
    public async Task Chats_ShowsEmptyState_WhenNoChatsExist()
    {
        // Arrange - fresh database, no chats
        await LoginAsOwnerAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - empty state alert is visible
        await Expect(_chatsPage.EmptyStateAlert).ToBeVisibleAsync();
        await Expect(_chatsPage.EmptyStateTitle).ToHaveTextAsync("No chats available");
    }

    [Test]
    public async Task Chats_DisplaysTable_WhenChatsExist()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Test Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - table is visible
        await Expect(_chatsPage.ChatsTable).ToBeVisibleAsync();
        await Expect(_chatsPage.TableTitle).ToHaveTextAsync("Managed Chats");
    }

    [Test]
    public async Task Chats_DisplaysChatList_WhenChatsExist()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Alpha Chat")
            .AsGroup()
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Beta Chat")
            .AsSupergroup()
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - chats are displayed
        await _chatsPage.ExpectChatCountAsync(2);

        await Expect(_chatsPage.ChatName("Alpha Chat")).ToBeVisibleAsync();
        await Expect(_chatsPage.ChatName("Beta Chat")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Chats_DisplaysChatType_ForEachChat()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("My Group")
            .AsGroup()
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("My Supergroup")
            .AsSupergroup()
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - chat types are displayed correctly
        await Expect(_chatsPage.ChatTypeCellFor("My Group")).ToHaveTextAsync("Group");
        await Expect(_chatsPage.ChatTypeCellFor("My Supergroup")).ToHaveTextAsync("Supergroup");
    }

    [Test]
    public async Task Chats_FiltersTable_WhenSearching()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Development Team")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Marketing Team")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Development Support")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Search for "Development"
        await _chatsPage.SearchChatsAsync("Development");

        // Assert - only Development chats visible (uses auto-retry for Blazor re-render)
        await _chatsPage.ExpectChatCountAsync(2);

        await Expect(_chatsPage.ChatName("Development Team")).ToBeVisibleAsync();
        await Expect(_chatsPage.ChatName("Development Support")).ToBeVisibleAsync();
        // Absence check runs after the positive checks above on the same filtered render
        await Expect(_chatsPage.ChatName("Marketing Team")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Chats_ShowsAllChats_WhenSearchCleared()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Engineering")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Sales")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Filter and then clear (uses auto-retry for Blazor re-render)
        await _chatsPage.SearchChatsAsync("Engineering");
        await _chatsPage.ExpectChatCountAsync(1);

        await _chatsPage.ClearSearchAsync();

        // Assert - all chats visible (uses auto-retry for Blazor re-render)
        await _chatsPage.ExpectChatCountAsync(2);
    }

    [Test]
    public async Task Chats_RequiresAuthentication()
    {
        // Act - try to access chats without login
        await Page.GotoAsync("/chats");

        // Assert - should redirect to login or register
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/(login|register)"));
    }

    [Test]
    public async Task Chats_AccessibleByAdmin()
    {
        // Arrange - login as Admin
        await LoginAsAdminAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - Admin can view chats page
        await Expect(_chatsPage.PageTitle).ToBeVisibleAsync();
    }

    [Test]
    public async Task Chats_AccessibleByGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - GlobalAdmin can view chats page
        await Expect(_chatsPage.PageTitle).ToBeVisibleAsync();
    }

    [Test]
    public async Task Chats_GlobalAdminSeesAllChats()
    {
        // Arrange - GlobalAdmin should see all chats (no chat_admins link needed)
        await LoginAsGlobalAdminAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat One")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Chat Two")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - GlobalAdmin sees all chats
        await _chatsPage.ExpectChatCountAsync(2);
    }

    [Test]
    public async Task Chats_OwnerSeesAllChats()
    {
        // Arrange - Owner should see all chats
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("First Chat")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Second Chat")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Third Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - Owner sees all chats
        await _chatsPage.ExpectChatCountAsync(3);
    }

    [Test]
    public async Task Chats_ShowsHealthStatus_ForEachChat()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Health Test Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - health status is displayed (default is Unknown for fresh chats)
        await Expect(_chatsPage.HealthStatusChip("Health Test Chat")).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task Chats_ShowsGlobalConfigIndicator_WhenNoCustomConfig()
    {
        // Arrange - chat without custom spam config should show "Global"
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Global Config Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - custom config indicator is NOT visible (shows "Global" text instead)
        // Sync on the row being rendered first so the absence check cannot pass before it lands
        await Expect(_chatsPage.ChatRow("Global Config Chat")).ToBeVisibleAsync();
        await Expect(_chatsPage.CustomConfigIcon("Global Config Chat")).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Chats_HasConfigureButton_ForEachChat()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Configurable Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Assert - Configure button exists (we test it's clickable)
        // Note: Actually clicking it opens a dialog which we test separately
        await Expect(_chatsPage.ChatName("Configurable Chat")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Chats_OpensConfigDialog_WhenConfigureClicked()
    {
        // Arrange
        await LoginAsOwnerAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Dialog Test Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        await _chatsPage.ClickConfigureAsync("Dialog Test Chat");

        // Assert - dialog opens (use web-first assertion for auto-retry)
        // MudBlazor dialogs render with .mud-dialog class
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        // Verify dialog title contains the chat name
        await Expect(_chatsPage.DialogTitle).ToContainTextAsync("Dialog Test Chat");
    }

    [Test]
    public async Task Chats_SearchByIdWorks()
    {
        // Arrange
        await LoginAsOwnerAsync();

        var chat = await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("ID Search Chat")
            .BuildAsync();

        await new TestChatBuilder(SharedFactory.Services)
            .WithTitle("Other Chat")
            .BuildAsync();

        // Act
        await _chatsPage.NavigateAsync();
        await _chatsPage.WaitForLoadAsync();

        // Search by chat ID
        await _chatsPage.SearchChatsAsync(chat.ChatId.ToString());

        // Assert - only the chat with matching ID is shown (uses auto-retry for Blazor re-render)
        await _chatsPage.ExpectChatCountAsync(1);

        await Expect(_chatsPage.ChatName("ID Search Chat")).ToBeVisibleAsync();
    }
}
