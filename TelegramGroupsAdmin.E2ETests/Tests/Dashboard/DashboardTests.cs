using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Dashboard;

/// <summary>
/// Tests for the Home/Dashboard page (/).
/// Verifies stats display, navigation, and permission-based visibility.
/// </summary>
[TestFixture]
public class DashboardTests : AuthenticatedTestBase
{
    private HomePage _homePage = null!;

    [SetUp]
    public void SetUp()
    {
        _homePage = new HomePage(Page);
    }

    [Test]
    public async Task Dashboard_LoadsSuccessfully_WhenAuthenticated()
    {
        // Arrange - login as owner
        await LoginAsOwnerAsync();

        // Act - navigate to dashboard
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - page loaded with correct title
        await Expect(_homePage.PageTitle).ToContainTextAsync(new Regex("Dashboard|Health"));
    }

    [Test]
    public async Task Dashboard_ShowsStatsSection_WhenLoaded()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - stats section is visible
        await Expect(_homePage.StatsGrid).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ShowsAllStatCards_WhenLoaded()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - all stat cards have values (even if 0 or N/A)
        await Expect(_homePage.StatValue("Total Messages")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Unique Users")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Images")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Data Range")).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task Dashboard_ShowsZeroStats_WhenNoMessages()
    {
        // Arrange - fresh database has no messages
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - stats should show 0 for a fresh database
        await Expect(_homePage.StatValue("Total Messages")).ToHaveTextAsync("0");
    }

    [Test]
    public async Task Dashboard_ShowsNoMessagesAlert_WhenDatabaseEmpty()
    {
        // Arrange - fresh database
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - info alert about no messages should show
        await Expect(_homePage.NoMessagesAlert).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ShowsQuickActionButtons()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - quick action buttons are visible
        await Expect(_homePage.ViewMessagesButton).ToBeVisibleAsync();
        await Expect(_homePage.RefreshButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ViewMessagesButton_NavigatesToMessages()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Act - click View Messages
        await _homePage.ClickViewMessagesAsync();

        // Assert - navigated to messages page
        await Expect(Page).ToHaveURLAsync(new Regex("/messages"));
    }

    [Test]
    public async Task Dashboard_RefreshButton_ReloadsData()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Act - click Refresh
        await _homePage.ClickRefreshAsync();

        // Assert - loading indicator appears briefly then data reloads
        // We verify by checking that the stats are still visible after refresh
        await _homePage.WaitForLoadAsync();
        await Expect(_homePage.StatsGrid).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_RequiresAuthentication()
    {
        // Act - try to access dashboard without login
        await Page.GotoAsync("/");

        // Assert - should redirect to login or register (first-run redirects to register)
        // The Home page checks IsFirstRunAsync() and redirects to /register if no users exist
        await Expect(Page).ToHaveURLAsync(new Regex("/(login|register)"));
    }

    [Test]
    public async Task Dashboard_AccessibleByAdmin()
    {
        // Arrange - login as Admin (lowest permission)
        await LoginAsAdminAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - Admin sees the scoped Overview cards AND the global card shells,
        // but the global cards are greyed placeholders with no data (interim UX).
        await Expect(_homePage.StatCard("Pending Reports")).ToBeVisibleAsync();
        await Expect(_homePage.StatPlaceholderCaption("Total Messages")).ToBeVisibleAsync();
        await Expect(_homePage.StatValue("Total Messages")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Dashboard_AccessibleByGlobalAdmin()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - GlobalAdmin can view dashboard
        await Expect(_homePage.StatsGrid).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_Admin_GlobalWidgetsGreyed_ScopedCardsReal()
    {
        // Arrange - login as Admin (chat-scoped permission)
        await LoginAsAdminAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - interim cross-chat-leak UX: the global card/panel shells render
        // for an Admin but are greyed placeholders with NO data, while scoped cards
        // (Pending Reports) show real values. The data-leak guarantee is that the
        // Admin sees the "GlobalAdmin only" placeholder and no numeric global value.
        // Each absence check follows a presence check on the same render, so it cannot pass early.

        // Global Total Messages card: shell present but greyed placeholder, no numeric value.
        await Expect(_homePage.StatPlaceholderCaption("Total Messages")).ToBeVisibleAsync();
        await Expect(_homePage.StatValue("Total Messages")).ToHaveCountAsync(0);

        // Global Recent Activity panel: present but the greyed placeholder, not a real list.
        await Expect(_homePage.ActivityFeedPlaceholderCaption).ToBeVisibleAsync();
        await Expect(_homePage.ActivityFeedItems).ToHaveCountAsync(0);

        // Scoped card shows a real value for Admin.
        await Expect(_homePage.StatValue("Pending Reports")).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task Dashboard_GlobalAdmin_ShowsAllWidgets()
    {
        // Arrange - login as GlobalAdmin
        await LoginAsGlobalAdminAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - GlobalAdmin sees the global widgets with real data (full dashboard):
        // Total Messages shows a real numeric value (not the placeholder) and Recent
        // Activity is the real panel (not the placeholder). The value/panel presence checks
        // come first so the placeholder absence checks cannot pass before the render lands.
        await Expect(_homePage.StatValue("Total Messages")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatPlaceholderCaption("Total Messages")).ToHaveCountAsync(0);

        await Expect(_homePage.ActivityFeedHeading).ToBeVisibleAsync();
        await Expect(_homePage.ActivityFeedPlaceholderCaption).ToHaveCountAsync(0);
    }

    #region Enhanced Dashboard Tests (#173)

    [Test]
    public async Task Dashboard_ShowsNewStatCards_WhenLoaded()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - new stat cards have values (even if 0)
        await Expect(_homePage.StatValue("Spam Today")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Active Bans")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Trusted Users")).Not.ToBeEmptyAsync();
        await Expect(_homePage.StatValue("Pending Reports")).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task Dashboard_ShowsZeroPendingReports_WhenNoPendingReports()
    {
        // Arrange - fresh database has 0 reports
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert
        await Expect(_homePage.StatValue("Pending Reports")).ToHaveTextAsync("0");
    }

    [Test]
    public async Task Dashboard_PendingReportsCard_NavigatesToReports_WhenClicked()
    {
        // Arrange - fresh database has 0 reports
        await LoginAsOwnerAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Act - click should navigate to reports (always clickable, even when 0)
        await _homePage.ClickPendingReportsCardAsync();

        // Assert - should navigate to reports page
        await Expect(Page).ToHaveURLAsync(new Regex("/reports"));
    }

    [Test]
    public async Task Dashboard_ShowsActivityFeedSection()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - Recent Activity section should be visible
        await Expect(_homePage.ActivityFeedHeading).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_ShowsEmptyActivityFeed_WhenNoActions()
    {
        // Arrange - fresh database
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - Activity feed section should show but be empty
        await Expect(_homePage.ActivityFeedHeading).ToBeVisibleAsync();
        // With no actions, the list item count should be 0
        await Expect(_homePage.ActivityFeedItems).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Dashboard_ShowsReviewReportsButton()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - Review Reports button should be visible
        await Expect(_homePage.ReviewReportsButton).ToBeVisibleAsync();
    }

    #endregion
}
