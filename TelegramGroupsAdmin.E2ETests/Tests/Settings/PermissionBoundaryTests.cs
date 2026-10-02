using static TelegramGroupsAdmin.E2ETests.PageObjects.BlazorPageExtensions;
using System.Text.RegularExpressions;
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

        // Sync on a link Admin does see so the absence check cannot pass before the nav renders
        await Expect(Page.Locator("a[href='/reports']")).ToBeVisibleAsync();

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

        // Sync on a link Admin does see so the absence check cannot pass before the nav renders
        await Expect(Page.Locator("a[href='/reports']")).ToBeVisibleAsync();

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

        // Sync on a link Admin does see so the absence check cannot pass before the nav renders
        await Expect(Page.Locator("a[href='/reports']")).ToBeVisibleAsync();

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

        // Assert - Settings is [Authorize(GlobalAdminOrOwner)], so an Admin is always redirected away
        // (server forbid -> /access-denied?ReturnUrl=%2Fsettings, or the router's NotAuthorized -> /login).
        // Retry until the redirect lands; the encoded ReturnUrl does not match the pattern.
        await Expect(Page).Not.ToHaveURLAsync(new Regex(@"/settings(?:[/?#]|$)"));
    }

    [Test]
    public async Task Admin_CannotAccessAuditLogPage()
    {
        // Arrange
        await LoginAsAdminAsync();

        // Act - Try to navigate directly to Audit Log
        await NavigateToAsync("/audit");

        // Assert - Audit is [Authorize(GlobalAdminOrOwner)], so an Admin is always redirected away
        // (server forbid -> /access-denied?ReturnUrl=%2Faudit, or the router's NotAuthorized -> /login).
        // Retry until the redirect lands; the encoded ReturnUrl does not match the pattern.
        await Expect(Page).Not.ToHaveURLAsync(new Regex(@"/audit(?:[?#]|$)"));
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

        // Assert - Should be able to access: the page body rendered past the Authorize gate and the URL
        // stayed on /settings (an unauthorised role is redirected away, see Admin_CannotAccessSettingsPage).
        // Settings.razor has no page-level access-denied alert to check for; the per-section
        // "Owner access required" alert is a different fact, asserted in GlobalAdmin_CannotSeeInfrastructureSettings.
        await Expect(settingsPage.SettingsSidebarHeading).ToBeVisibleAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/settings(?:[/?#]|$)"));
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

        // Sync on the Logging link (shown to every role in the same System nav group) so the
        // absence check cannot pass before the nav renders
        await Expect(settingsPage.LoggingSettingsLink).ToBeVisibleAsync();

        // Assert - GlobalAdmin keeps the Admin Accounts link (gated on IsGlobalAdminOrHigher)...
        await Expect(settingsPage.AdminAccountsLink).ToBeVisibleAsync();

        // ...but the Owner-only infrastructure links are hidden
        await Expect(settingsPage.GeneralSettingsLink).Not.ToBeVisibleAsync();

        // ...and the default section (General, infrastructure) shows the Owner-access-required alert in place of its body
        await Expect(settingsPage.OwnerAccessRequiredAlert).ToBeVisibleAsync();
    }

    [Test]
    public async Task GlobalAdmin_CanAccessAuditLog()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await NavigateToAsync("/audit");
        await Page.WaitForInteractiveAsync();

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
        await Expect(settingsPage.GeneralSettingsLink).ToBeVisibleAsync(new() { Timeout = 5000 });
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
        await Page.WaitForInteractiveAsync();

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
        await Page.WaitForInteractiveAsync();

        // Assert - Should be able to access (page title visible)
        var pageTitle = Page.Locator(".mud-typography-h4");
        await Expect(pageTitle).ToBeVisibleAsync();
    }

    #endregion
}
