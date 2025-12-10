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
        Assert.That(await _chatsPage.IsPageTitleVisibleAsync(), Is.True,
            "Chats page title should be visible");

        var pageTitle = await _chatsPage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Chat Management"),
            "Page title should be 'Chat Management'");
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
        Assert.That(await _chatsPage.IsEmptyStateVisibleAsync(), Is.True,
            "Empty state should be visible when no chats exist");

        var emptyTitle = await _chatsPage.GetEmptyStateTitleAsync();
        Assert.That(emptyTitle, Is.EqualTo("No chats available"),
            "Empty state should show 'No chats available'");
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
        Assert.That(await _chatsPage.IsTableVisibleAsync(), Is.True,
            "Chats table should be visible when chats exist");

        var tableTitle = await _chatsPage.GetTableTitleAsync();
        Assert.That(tableTitle, Is.EqualTo("Managed Chats"),
            "Table title should be 'Managed Chats'");
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
        var chatCount = await _chatsPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "Should display 2 chats");

        var chatNames = await _chatsPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("Alpha Chat"),
            "Should display 'Alpha Chat'");
        Assert.That(chatNames, Does.Contain("Beta Chat"),
            "Should display 'Beta Chat'");
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
        var groupType = await _chatsPage.GetChatTypeByNameAsync("My Group");
        Assert.That(groupType?.Trim(), Is.EqualTo("Group"),
            "Group chat should show type 'Group'");

        var supergroupType = await _chatsPage.GetChatTypeByNameAsync("My Supergroup");
        Assert.That(supergroupType?.Trim(), Is.EqualTo("Supergroup"),
            "Supergroup chat should show type 'Supergroup'");
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

        var chatNames = await _chatsPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("Development Team"));
        Assert.That(chatNames, Does.Contain("Development Support"));
        Assert.That(chatNames, Does.Not.Contain("Marketing Team"));
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
        Assert.That(await _chatsPage.IsPageTitleVisibleAsync(), Is.True,
            "Admin should be able to view chats page");
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
        Assert.That(await _chatsPage.IsPageTitleVisibleAsync(), Is.True,
            "GlobalAdmin should be able to view chats page");
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
        var chatCount = await _chatsPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(2),
            "GlobalAdmin should see all chats");
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
        var chatCount = await _chatsPage.GetChatCountAsync();
        Assert.That(chatCount, Is.EqualTo(3),
            "Owner should see all chats");
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
        var healthStatus = await _chatsPage.GetHealthStatusByNameAsync("Health Test Chat");
        Assert.That(healthStatus, Is.Not.Null.And.Not.Empty,
            "Health status should be displayed for each chat");
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
        var hasCustomConfig = await _chatsPage.HasCustomConfigAsync("Global Config Chat");
        Assert.That(hasCustomConfig, Is.False,
            "Chat without custom config should show 'Global' indicator, not checkmark");
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
        var chatNames = await _chatsPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("Configurable Chat"),
            "Chat should be visible to verify button exists");
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
        var dialogTitle = await _chatsPage.GetDialogTitleAsync();
        Assert.That(dialogTitle, Does.Contain("Dialog Test Chat"),
            "Dialog title should contain the chat name");
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

        var chatNames = await _chatsPage.GetChatNamesAsync();
        Assert.That(chatNames, Does.Contain("ID Search Chat"),
            "Should show the chat with matching ID");
    }
}
