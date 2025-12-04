using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects.Settings;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Settings;

/// <summary>
/// Tests for the Web Admin Accounts settings section (/settings/system/accounts).
/// Verifies user management, invite creation, and account actions.
/// Note: This section requires GlobalAdmin or Owner role to access.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class WebAdminAccountsTests : AuthenticatedTestBase
{
    private WebAdminAccountsPage _accountsPage = null!;

    [SetUp]
    public void SetUp()
    {
        _accountsPage = new WebAdminAccountsPage(Page);
    }

    #region Page Load Tests

    [Test]
    public async Task PageLoads_ShowsUserTable_WhenGlobalAdmin()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert - User table should be visible with headers
        Assert.That(await _accountsPage.IsUserTableVisibleAsync(), Is.True,
            "User table should be visible");

        var headers = await _accountsPage.GetTableHeadersAsync();
        Assert.That(headers, Does.Contain("Email"));
        Assert.That(headers, Does.Contain("Permission Level"));
        Assert.That(headers, Does.Contain("Status"));
    }

    [Test]
    public async Task PageLoads_ShowsUserTable_WhenOwner()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert - User table should be visible
        Assert.That(await _accountsPage.IsUserTableVisibleAsync(), Is.True,
            "User table should be visible for Owner");
    }

    [Test]
    public async Task PageLoads_ShowsCurrentUser()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert - At least one user should be displayed
        var userCount = await _accountsPage.GetUserCountAsync();
        Assert.That(userCount, Is.GreaterThanOrEqualTo(1),
            "At least the current user should be displayed");
    }

    #endregion

    #region Action Button Visibility Tests

    [Test]
    public async Task GlobalAdmin_CanSeeCreateUserButton()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _accountsPage.IsCreateUserButtonVisibleAsync(), Is.True,
            "GlobalAdmin should see Create User button");
    }

    [Test]
    public async Task GlobalAdmin_CanSeeManageInvitesButton()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _accountsPage.IsManageInvitesButtonVisibleAsync(), Is.True,
            "GlobalAdmin should see Manage Invites button");
    }

    [Test]
    public async Task Owner_CanSeeAllActionButtons()
    {
        // Arrange
        await LoginAsOwnerAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _accountsPage.IsCreateUserButtonVisibleAsync(), Is.True,
            "Owner should see Create User button");
        Assert.That(await _accountsPage.IsManageInvitesButtonVisibleAsync(), Is.True,
            "Owner should see Manage Invites button");
    }

    #endregion

    #region Status Filter Tests

    [Test]
    public async Task StatusFilter_IsVisible()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();

        // Act
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Assert
        Assert.That(await _accountsPage.IsStatusFilterVisibleAsync(), Is.True,
            "Status filter should be visible");
    }

    #endregion

    #region Create User Dialog Tests

    [Test]
    public async Task CreateUser_OpensDialog_WhenClicked()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Act
        await _accountsPage.ClickCreateUserAsync();

        // Assert
        Assert.That(await _accountsPage.IsDialogOpenAsync(), Is.True,
            "Create User dialog should open");

        var dialogTitle = await _accountsPage.GetDialogTitleAsync();
        Assert.That(dialogTitle, Does.Contain("Create User").Or.Contain("Invite"),
            "Dialog title should indicate user creation");
    }

    [Test]
    public async Task CreateUser_GlobalAdmin_SeesLimitedPermissionOptions()
    {
        // Arrange - GlobalAdmin cannot create Owner accounts
        await LoginAsGlobalAdminAsync();
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Act - ClickCreateUserAsync now waits for dialog to appear
        await _accountsPage.ClickCreateUserAsync();

        // Assert - GlobalAdmin should see Admin and GlobalAdmin options, but NOT Owner
        var options = await _accountsPage.GetPermissionOptionsAsync();

        // Should have Admin and GlobalAdmin
        Assert.That(options.Any(o => o.Contains("Admin")), Is.True,
            "GlobalAdmin should see Admin option");

        // Should NOT see Owner option
        Assert.That(options.Any(o => o == "Owner" || o.Contains("Owner")), Is.False,
            "GlobalAdmin should NOT see Owner option");

        // Cleanup
        await _accountsPage.CloseDialogAsync();
    }

    [Test]
    public async Task CreateUser_Owner_SeesAllPermissionOptions()
    {
        // Arrange - Owner can create any account type
        await LoginAsOwnerAsync();
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Act - ClickCreateUserAsync now waits for dialog to appear
        await _accountsPage.ClickCreateUserAsync();

        // Assert - Owner should see all options including Owner
        var options = await _accountsPage.GetPermissionOptionsAsync();

        Assert.That(options.Any(o => o.Contains("Admin")), Is.True,
            "Owner should see Admin option");
        Assert.That(options.Any(o => o.Contains("Owner")), Is.True,
            "Owner should see Owner option");

        // Cleanup
        await _accountsPage.CloseDialogAsync();
    }

    #endregion

    #region Manage Invites Dialog Tests

    [Test]
    public async Task ManageInvites_OpensDialog_WhenClicked()
    {
        // Arrange
        await LoginAsGlobalAdminAsync();
        await _accountsPage.NavigateAsync();
        await _accountsPage.WaitForLoadAsync();

        // Act
        await _accountsPage.ClickManageInvitesAsync();

        // Assert
        Assert.That(await _accountsPage.IsDialogOpenAsync(), Is.True,
            "Manage Invites dialog should open");

        var dialogTitle = await _accountsPage.GetDialogTitleAsync();
        Assert.That(dialogTitle, Does.Contain("Invite"),
            "Dialog title should mention invites");
    }

    #endregion
}
