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
        var title = await _homePage.GetPageTitleAsync();
        Assert.That(title, Does.Contain("Dashboard").Or.Contain("Health"),
            "Page should show dashboard title");
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
        Assert.That(await _homePage.AreStatsVisibleAsync(), Is.True,
            "Stats section should be visible after loading");
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
        var totalMessages = await _homePage.GetTotalMessagesAsync();
        var uniqueUsers = await _homePage.GetUniqueUsersAsync();
        var imagesCount = await _homePage.GetImagesCountAsync();
        var dataRange = await _homePage.GetDataRangeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(totalMessages, Is.Not.Null.And.Not.Empty,
                "Total Messages stat should have a value");
            Assert.That(uniqueUsers, Is.Not.Null.And.Not.Empty,
                "Unique Users stat should have a value");
            Assert.That(imagesCount, Is.Not.Null.And.Not.Empty,
                "Images stat should have a value");
            Assert.That(dataRange, Is.Not.Null.And.Not.Empty,
                "Data Range stat should have a value");
        });
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
        var totalMessages = await _homePage.GetTotalMessagesAsync();
        Assert.That(totalMessages, Is.EqualTo("0"),
            "Fresh database should show 0 total messages");
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
        Assert.That(await _homePage.IsNoMessagesAlertVisibleAsync(), Is.True,
            "Should show info alert when no messages are cached");
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
        // Note: Using sequential assertions instead of Assert.Multiple because
        // async lambdas don't work correctly with Assert.Multiple (NUnit doesn't await them)
        Assert.That(await _homePage.IsViewMessagesButtonVisibleAsync(), Is.True,
            "View Messages button should be visible");
        Assert.That(await _homePage.IsRefreshStatsButtonVisibleAsync(), Is.True,
            "Refresh Stats button should be visible");
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
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/messages"));
    }

    [Test]
    public async Task Dashboard_RefreshStats_ReloadsData()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Act - click Refresh Stats
        await _homePage.ClickRefreshStatsAsync();

        // Assert - loading indicator appears briefly then data reloads
        // We verify by checking that the stats are still visible after refresh
        await _homePage.WaitForLoadAsync();
        Assert.That(await _homePage.AreStatsVisibleAsync(), Is.True,
            "Stats should still be visible after refresh");
    }

    [Test]
    public async Task Dashboard_RequiresAuthentication()
    {
        // Act - try to access dashboard without login
        await Page.GotoAsync("/");

        // Assert - should redirect to login or register (first-run redirects to register)
        // The Home page checks IsFirstRunAsync() and redirects to /register if no users exist
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/(login|register)"));
    }

    [Test]
    public async Task Dashboard_AccessibleByAdmin()
    {
        // Arrange - login as Admin (lowest permission)
        await LoginAsAdminAsync();

        // Act
        await _homePage.NavigateAsync();
        await _homePage.WaitForLoadAsync();

        // Assert - Admin can view dashboard
        Assert.That(await _homePage.AreStatsVisibleAsync(), Is.True,
            "Admin should be able to view dashboard stats");
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
        Assert.That(await _homePage.AreStatsVisibleAsync(), Is.True,
            "GlobalAdmin should be able to view dashboard stats");
    }
}
