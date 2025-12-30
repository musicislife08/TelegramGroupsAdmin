using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// WASM Tests for the Profile page (/profile).
/// Verifies account info display, password change, TOTP status, and Telegram account linking.
/// Uses WasmSharedAuthenticatedTestBase for faster test execution with shared factory.
///
/// NOTE: These tests use the same ProfilePage page object as Blazor Server tests.
/// The WASM Profile page should produce identical HTML structure.
/// Any selector failures indicate UI regression that needs to be fixed in the WASM component.
/// </summary>
[TestFixture]
public class WasmProfileTests : WasmSharedAuthenticatedTestBase
{
    private WasmProfilePage _profilePage = null!;

    [SetUp]
    public void SetUp()
    {
        _profilePage = new WasmProfilePage(Page);
    }

    #region Account Information Tests

    [Test]
    public async Task Profile_PageLoads_ShowsAccountInfo()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();

        // Assert - page loads with correct title (using page object method)
        Assert.That(await _profilePage.IsPageTitleVisibleAsync(), Is.True,
            "Page title should be visible");

        var pageTitle = await _profilePage.GetPageTitleAsync();
        Assert.That(pageTitle, Is.EqualTo("Profile Settings"),
            "Page title should be 'Profile Settings'");

        // Verify all sections are visible using Playwright Expect (auto-retry)
        await _profilePage.AssertAccountInfoSectionVisibleAsync();
        await _profilePage.AssertChangePasswordSectionVisibleAsync();
        await _profilePage.AssertTotpSectionVisibleAsync();
        await _profilePage.AssertTelegramLinkingSectionVisibleAsync();

        // Verify account info fields are populated
        Assert.That(await _profilePage.HasAccountInfoFieldsAsync(), Is.True,
            "All account info fields should be visible");
    }

    #endregion

    #region Change Password Tests

    [Test]
    public async Task Profile_ChangePassword_RequiresAllFields()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();

        // Wait for page to be ready
        await _profilePage.AssertChangePasswordSectionVisibleAsync();

        // Act - click Change Password without filling any fields
        await _profilePage.ClickChangePasswordButtonAsync();

        // Assert - should show validation error (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("Please fill in all fields"),
            "Should show 'Please fill in all fields' error");
    }

    [Test]
    public async Task Profile_ChangePassword_ValidatesPasswordMatch()
    {
        // Arrange - login as Owner
        var owner = await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertChangePasswordSectionVisibleAsync();

        // Act - fill mismatched passwords
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: "NewPassword123!",
            confirmPassword: "DifferentPassword456!");

        // Assert - should show mismatch error (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("New passwords do not match"),
            "Should show password mismatch error");
    }

    [Test]
    public async Task Profile_ChangePassword_ValidatesMinLength()
    {
        // Arrange - login as Owner
        var owner = await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertChangePasswordSectionVisibleAsync();

        // Act - fill password shorter than 8 characters
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: "Short1!",
            confirmPassword: "Short1!");

        // Assert - should show length error (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("at least 8 characters"),
            "Should show minimum length error");
    }

    [Test]
    public async Task Profile_ChangePassword_WrongCurrentPassword()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertChangePasswordSectionVisibleAsync();

        // Act - fill wrong current password
        await _profilePage.ChangePasswordAsync(
            currentPassword: "WrongPassword123!",
            newPassword: "NewValidPassword123!",
            confirmPassword: "NewValidPassword123!");

        // Assert - should show incorrect password error (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("Current password is incorrect"),
            "Should show incorrect password error");
    }

    [Test]
    public async Task Profile_ChangePassword_Success()
    {
        // Arrange - login as Owner
        var owner = await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertChangePasswordSectionVisibleAsync();

        var newPassword = TestCredentials.GeneratePassword();

        // Act - change password correctly
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: newPassword,
            confirmPassword: newPassword);

        // Assert - should show success message (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("Password changed successfully"),
            "Should show password change success message");

        // Verify form fields are cleared
        var currentPasswordValue = await _profilePage.GetCurrentPasswordValueAsync();
        Assert.That(currentPasswordValue, Is.Empty,
            "Current password field should be cleared after successful change");
    }

    #endregion

    #region TOTP Tests

    [Test]
    public async Task Profile_Totp_ShowsDisabledState_WhenNotEnabled()
    {
        // Arrange - login as Owner (TOTP disabled by default in tests)
        await LoginAsOwnerAsync();

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTotpSectionVisibleAsync();

        // Assert - TOTP section shows disabled state
        Assert.That(await _profilePage.IsTotpDisabledAsync(), Is.True,
            "Should show '2FA is not enabled' warning");
        Assert.That(await _profilePage.IsEnable2FAButtonVisibleAsync(), Is.True,
            "Enable 2FA button should be visible");
        Assert.That(await _profilePage.IsReset2FAButtonVisibleAsync(), Is.False,
            "Reset 2FA button should NOT be visible when TOTP is disabled");
    }

    [Test]
    public async Task Profile_Totp_ShowsEnabledState_WhenEnabled()
    {
        // Arrange - create user with TOTP enabled and login via cookie injection
        var totpUser = await new WasmTestUserBuilder(SharedFactory.Services)
            .AsOwner()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .BuildAsync();

        await LoginAsAsync(totpUser);

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTotpSectionVisibleAsync();

        // Assert - TOTP section shows enabled state
        Assert.That(await _profilePage.IsTotpEnabledAsync(), Is.True,
            "Should show '2FA is currently enabled' alert");
        Assert.That(await _profilePage.IsReset2FAButtonVisibleAsync(), Is.True,
            "Reset 2FA button should be visible");
        Assert.That(await _profilePage.IsEnable2FAButtonVisibleAsync(), Is.False,
            "Enable 2FA button should NOT be visible when TOTP is enabled");
    }

    [Test]
    public async Task Profile_Totp_EnableFlow_OpensDialog()
    {
        // Arrange - login as Owner (TOTP disabled)
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTotpSectionVisibleAsync();

        // Act - click Enable 2FA button
        await _profilePage.ClickEnable2FAButtonAsync();

        // Assert - dialog opens with expected elements (using page object methods)
        Assert.That(await _profilePage.IsTotpSetupDialogVisibleAsync(), Is.True,
            "TOTP setup dialog should be visible");
        Assert.That(await _profilePage.IsTotpQRCodeVisibleAsync(), Is.True,
            "QR code should be visible in the dialog");
        Assert.That(await _profilePage.IsTotpManualKeyVisibleAsync(), Is.True,
            "Manual entry key section should be visible");

        var verificationInput = _profilePage.GetTotpVerificationCodeInput();
        await Expect(verificationInput).ToBeVisibleAsync();

        // Clean up - close dialog
        await _profilePage.CancelTotpSetupDialogAsync();
    }

    #endregion

    #region Telegram Linking Tests

    [Test]
    public async Task Profile_TelegramLinking_ShowsNoAccountsMessage()
    {
        // Arrange - login as Owner (no linked accounts)
        await LoginAsOwnerAsync();

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTelegramLinkingSectionVisibleAsync();

        // Assert - shows no accounts message
        Assert.That(await _profilePage.IsNoLinkedAccountsMessageVisibleAsync(), Is.True,
            "Should show 'No Telegram accounts linked' message");
        Assert.That(await _profilePage.IsLinkedAccountsTableVisibleAsync(), Is.False,
            "Linked accounts table should NOT be visible when no accounts are linked");
    }

    [Test]
    public async Task Profile_TelegramLinking_GenerateToken()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTelegramLinkingSectionVisibleAsync();

        // Act - click Link New Telegram Account
        await _profilePage.ClickLinkNewAccountButtonAsync();

        // Assert - token is generated and displayed (using page object Expect method)
        await _profilePage.AssertLinkTokenVisibleAsync();

        var token = await _profilePage.GetLinkTokenValueAsync();
        Assert.That(token, Is.Not.Null.And.Not.Empty,
            "Generated token should not be empty");
        Assert.That(token!.Length, Is.EqualTo(12),
            "Generated token should be 12 characters");

        // Verify the /link command instruction is visible
        var linkCommandText = Page.GetByText("/link");
        await Expect(linkCommandText).ToBeVisibleAsync();
    }

    [Test]
    public async Task Profile_TelegramLinking_ShowsLinkedAccounts()
    {
        // Arrange - login as Owner and create linked Telegram account
        var owner = await LoginAsOwnerAsync();

        // Create a linked Telegram account
        await new TestTelegramUserMappingBuilder(SharedFactory.Services)
            .WithTelegramId(123456789)
            .WithTelegramUsername("testlinkeduser")
            .LinkedToWebUser(owner.Id)
            .BuildAsync();

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();
        await _profilePage.AssertTelegramLinkingSectionVisibleAsync();

        // Assert - linked accounts table is visible
        Assert.That(await _profilePage.IsLinkedAccountsTableVisibleAsync(), Is.True,
            "Linked accounts table should be visible");
        Assert.That(await _profilePage.IsNoLinkedAccountsMessageVisibleAsync(), Is.False,
            "No accounts message should NOT be visible when accounts are linked");

        // Verify the linked account details
        var count = await _profilePage.GetLinkedAccountsCountAsync();
        Assert.That(count, Is.EqualTo(1),
            "Should have exactly 1 linked account");

        Assert.That(await _profilePage.HasLinkedAccountWithUsernameAsync("@testlinkeduser"), Is.True,
            "Should show the linked account's username");
    }

    [Test]
    public async Task Profile_TelegramLinking_UnlinkAccount()
    {
        // Arrange - login as Owner and create linked Telegram account
        var owner = await LoginAsOwnerAsync();

        await new TestTelegramUserMappingBuilder(SharedFactory.Services)
            .WithTelegramId(987654321)
            .WithTelegramUsername("unlinkme")
            .LinkedToWebUser(owner.Id)
            .BuildAsync();

        await _profilePage.NavigateAsync();
        await _profilePage.AssertTelegramLinkingSectionVisibleAsync();

        // Verify account is initially linked
        Assert.That(await _profilePage.GetLinkedAccountsCountAsync(), Is.EqualTo(1),
            "Should have 1 linked account before unlinking");

        // Act - click Unlink button
        await _profilePage.ClickUnlinkButtonAsync(0);

        // Assert - account is unlinked (using page object snackbar method)
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("unlinked successfully"),
            "Should show unlink success message");

        // Verify account is removed from the table (using Expect for auto-retry after page reload)
        await _profilePage.AssertNoLinkedAccountsMessageVisibleAsync();
    }

    #endregion
}
