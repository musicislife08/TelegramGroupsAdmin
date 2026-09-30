using System.Text.RegularExpressions;
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
        await Expect(_accountsPage.UserTable).ToBeVisibleAsync();

        await Expect(_accountsPage.TableHeader("Email")).ToBeVisibleAsync();
        await Expect(_accountsPage.TableHeader("Permission Level")).ToBeVisibleAsync();
        await Expect(_accountsPage.TableHeader("Status")).ToBeVisibleAsync();
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
        await Expect(_accountsPage.UserTable).ToBeVisibleAsync();
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
        await Expect(_accountsPage.UserRows.First).ToBeVisibleAsync();
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
        await Expect(_accountsPage.CreateUserButton).ToBeVisibleAsync();
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
        await Expect(_accountsPage.ManageInvitesButton).ToBeVisibleAsync();
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
        await Expect(_accountsPage.CreateUserButton).ToBeVisibleAsync();
        await Expect(_accountsPage.ManageInvitesButton).ToBeVisibleAsync();
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
        await Expect(_accountsPage.StatusFilter).ToBeVisibleAsync();
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
        await Expect(_accountsPage.Dialog).ToBeVisibleAsync();

        // Dialog title should indicate user creation
        await Expect(_accountsPage.DialogTitle).ToContainTextAsync(new Regex("Create User|Invite"));
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
        await _accountsPage.OpenPermissionOptionsAsync();

        // Should have Admin and GlobalAdmin (also the positive render sync for the absence check below)
        await Expect(_accountsPage.PermissionOptions.Filter(new() { HasTextRegex = new Regex("Admin") }).First).ToBeVisibleAsync();

        // Should NOT see Owner option
        await Expect(_accountsPage.PermissionOptions.Filter(new() { HasTextRegex = new Regex("Owner") })).ToHaveCountAsync(0);

        // Cleanup
        await _accountsPage.ClosePermissionOptionsAsync();
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
        await _accountsPage.OpenPermissionOptionsAsync();

        await Expect(_accountsPage.PermissionOptions.Filter(new() { HasTextRegex = new Regex("Admin") }).First).ToBeVisibleAsync();
        await Expect(_accountsPage.PermissionOptions.Filter(new() { HasTextRegex = new Regex("Owner") }).First).ToBeVisibleAsync();

        // Cleanup
        await _accountsPage.ClosePermissionOptionsAsync();
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
        await Expect(_accountsPage.Dialog).ToBeVisibleAsync();

        // Dialog title should mention invites
        await Expect(_accountsPage.DialogTitle).ToContainTextAsync("Invite");
    }

    #endregion
}
