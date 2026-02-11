using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Tests for permission boundaries - verifying that different user roles
/// see appropriate navigation items and can/cannot access restricted pages.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class PermissionBoundaryTests : AuthenticatedTestBase
{
    #region Navigation Menu Tests

    [Test]
    public async Task Admin_CanSeeReportsAndUsersInNavMenu()
    {
        // Arrange
        await LoginAsAdminAsync();
        await NavigateToAsync("/");

        // Act - Check nav menu items
        var reportsLink = Page.Locator("a[href='/reports']");
        var usersLink = Page.Locator("a[href='/users']");

        // Assert - Admin should see Reports and Users
        await Expect(reportsLink).ToBeVisibleAsync();
        await Expect(usersLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CannotSeeSettingsInNavMenu()
    {
        // Arrange
        await LoginAsAdminAsync();
        await NavigateToAsync("/");

        // Act - Check for Settings link
        var settingsLink = Page.Locator("a[href='/settings']");

        // Assert - Admin should NOT see Settings
        await Expect(settingsLink).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CannotSeeAuditLogInNavMenu()
    {
        // Arrange
        await LoginAsAdminAsync();
        await NavigateToAsync("/");

        // Act - Check for Audit Log link
        var auditLink = Page.Locator("a[href='/audit']");

        // Assert - Admin should NOT see Audit Log
        await Expect(auditLink).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_CannotSeeChatManagementInNavMenu()
    {
        // Arrange
        await LoginAsAdminAsync();
        await NavigateToAsync("/");

        // Act - Check for Chat Management link
        var chatsLink = Page.Locator("a[href='/chats']");

        // Assert - Admin should NOT see Chat Management
        await Expect(chatsLink).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task GlobalAdmin_CanSeeAllNavMenuItems()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();
        await NavigateToAsync("/");

        // Act - Check all admin nav items
        var reportsLink = Page.Locator("a[href='/reports']");
        var usersLink = Page.Locator("a[href='/users']");
        var settingsLink = Page.Locator("a[href='/settings']");
        var auditLink = Page.Locator("a[href='/audit']");
        var chatsLink = Page.Locator("a[href='/chats']");

        // Assert - GlobalAdmin should see all
        await Expect(reportsLink).ToBeVisibleAsync();
        await Expect(usersLink).ToBeVisibleAsync();
        await Expect(settingsLink).ToBeVisibleAsync();
        await Expect(auditLink).ToBeVisibleAsync();
        await Expect(chatsLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task Owner_CanSeeAllNavMenuItems()
    {
        // Arrange
        await LoginAsOwnerAsync();
        await NavigateToAsync("/");

        // Act - Check all admin nav items
        var reportsLink = Page.Locator("a[href='/reports']");
        var usersLink = Page.Locator("a[href='/users']");
        var settingsLink = Page.Locator("a[href='/settings']");
        var auditLink = Page.Locator("a[href='/audit']");
        var chatsLink = Page.Locator("a[href='/chats']");

        // Assert - Owner should see all
        await Expect(reportsLink).ToBeVisibleAsync();
        await Expect(usersLink).ToBeVisibleAsync();
        await Expect(settingsLink).ToBeVisibleAsync();
        await Expect(auditLink).ToBeVisibleAsync();
        await Expect(chatsLink).ToBeVisibleAsync();
    }

    #endregion

    #region Page Access Tests

    [Test]
    public async Task Admin_CannotAccessSettingsPage()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act - Try to navigate directly to Settings
        await NavigateToAsync("/settings");

        // Assert - Should be denied or redirected
        // The page should show access denied or redirect to another page
        var currentUrl = Page.Url;
        var isOnSettings = currentUrl.Contains("/settings");

        if (isOnSettings)
        {
            // If still on settings, should show access denied message
            var accessDenied = Page.GetByText("access denied", new PageGetByTextOptions { Exact = false });
            var ownerRequired = Page.GetByText("Owner access required", new PageGetByTextOptions { Exact = false });
            var errorAlert = Page.Locator(".mud-alert-error");

            var hasAccessDenied = await accessDenied.Or(ownerRequired).Or(errorAlert).IsVisibleAsync();
            Assert.That(hasAccessDenied, Is.True, "Admin should see access denied on Settings page");
        }
        // If redirected away, that's also acceptable
    }

    [Test]
    public async Task Admin_CannotAccessAuditLogPage()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act - Try to navigate directly to Audit Log
        await NavigateToAsync("/audit");

        // Assert - Should be denied or redirected
        var currentUrl = Page.Url;

        // Check if redirected to access-denied page or shows error
        var isAccessDenied = currentUrl.Contains("/access-denied") ||
                             await Page.GetByText("Access Denied").IsVisibleAsync();

        Assert.That(isAccessDenied || !currentUrl.Contains("/audit"),
            Is.True, "Admin should not be able to access Audit Log page");
    }

    [Test]
    public async Task GlobalAdmin_CanAccessSettingsPage()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        var settingsPage = new SettingsPage(Page);
        await settingsPage.NavigateAsync();
        await settingsPage.WaitForLoadAsync();

        // Assert - Should be able to access (no access denied)
        var isAllowed = await settingsPage.IsAccessAllowedAsync();
        Assert.That(isAllowed, Is.True, "GlobalAdmin should be able to access Settings page");
    }

    [Test]
    public async Task GlobalAdmin_CannotSeeInfrastructureSettings()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        var settingsPage = new SettingsPage(Page);
        await settingsPage.NavigateAsync();
        await settingsPage.WaitForLoadAsync();

        // Assert - Infrastructure settings should be hidden
        var hasInfrastructure = await settingsPage.AreInfrastructureSettingsVisibleAsync();
        Assert.That(hasInfrastructure, Is.False, "GlobalAdmin should NOT see infrastructure settings");
    }

    [Test]
    public async Task GlobalAdmin_CanAccessAuditLog()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await NavigateToAsync("/audit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - Should be on audit page
        var pageTitle = Page.Locator(".mud-typography-h4");
        await Expect(pageTitle).ToContainTextAsync("Audit");
    }

    [Test]
    public async Task Owner_CanAccessAllInfrastructureSettings()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        var settingsPage = new SettingsPage(Page);
        await settingsPage.NavigateAsync();
        await settingsPage.WaitForLoadAsync();

        // Assert - Owner should see infrastructure settings
        var hasInfrastructure = await settingsPage.AreInfrastructureSettingsVisibleAsync();
        Assert.That(hasInfrastructure, Is.True, "Owner should see infrastructure settings");
    }

    #endregion

    #region Direct URL Access Tests

    [Test]
    public async Task Admin_CanAccessReportsPage()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await NavigateToAsync("/reports");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - Should be able to access (page title visible)
        var pageTitle = Page.Locator(".mud-typography-h4");
        await Expect(pageTitle).ToContainTextAsync("Reports");
    }

    [Test]
    public async Task Admin_CanAccessUsersPage()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act
        await NavigateToAsync("/users");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Assert - Should be able to access (page title visible)
        var pageTitle = Page.Locator(".mud-typography-h4");
        await Expect(pageTitle).ToBeVisibleAsync();
    }

    #endregion
}
