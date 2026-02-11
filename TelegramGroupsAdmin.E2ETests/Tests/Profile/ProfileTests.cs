using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Profile;

/// <summary>
/// Tests for the Profile page (/profile).
/// Verifies account info display, password change, TOTP status, and Telegram account linking.
/// Uses SharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class ProfileTests : SharedAuthenticatedTestBase
{
    private ProfilePage _profilePage = null!;

    [SetUp]
    public void SetUp()
    {
        _profilePage = new ProfilePage(Page);
    }

    #region Account Information Tests

    [Test]
    public async Task Profile_PageLoads_ShowsAccountInfo()
    {
        // Arrange - login as Owner
        var owner = await LoginAsOwnerAsync();

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();

        // Assert - page loads with correct title
        Assert.That(await _profilePage.IsPageTitleVisibleAsync(), Is.True,
            "Profile page title should be visible");

        var pageTitle = await _profilePage.GetPageTitleAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(pageTitle, Is.EqualTo("Profile Settings"),
                      "Page title should be 'Profile Settings'");

            // Verify all sections are visible
            Assert.That(await _profilePage.IsAccountInfoSectionVisibleAsync(), Is.True,
                "Account Information section should be visible");
            Assert.That(await _profilePage.IsChangePasswordSectionVisibleAsync(), Is.True,
                "Change Password section should be visible");
            Assert.That(await _profilePage.IsTotpSectionVisibleAsync(), Is.True,
                "TOTP section should be visible");
            Assert.That(await _profilePage.IsTelegramLinkingSectionVisibleAsync(), Is.True,
                "Telegram Linking section should be visible");

            // Verify account info fields are populated
            Assert.That(await _profilePage.HasAccountInfoFieldsAsync(), Is.True,
                "All account info fields should be visible");
        }
    }

    #endregion

    #region Change Password Tests

    [Test]
    public async Task Profile_ChangePassword_RequiresAllFields()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();

        // Act - click Change Password without filling any fields
        await _profilePage.ClickChangePasswordButtonAsync();

        // Assert - should show validation error
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

        // Act - fill mismatched passwords
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: "NewPassword123!",
            confirmPassword: "DifferentPassword456!");

        // Assert - should show mismatch error
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

        // Act - fill password shorter than 8 characters
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: "Short1!",
            confirmPassword: "Short1!");

        // Assert - should show length error
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

        // Act - fill wrong current password
        await _profilePage.ChangePasswordAsync(
            currentPassword: "WrongPassword123!",
            newPassword: "NewValidPassword123!",
            confirmPassword: "NewValidPassword123!");

        // Assert - should show incorrect password error
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

        var newPassword = TestCredentials.GeneratePassword();

        // Act - change password correctly
        await _profilePage.ChangePasswordAsync(
            currentPassword: owner.Password,
            newPassword: newPassword,
            confirmPassword: newPassword);

        // Assert - should show success message
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

        using (Assert.EnterMultipleScope())
        {
            // Assert - TOTP section shows disabled state
            Assert.That(await _profilePage.IsTotpDisabledAsync(), Is.True,
                "Should show '2FA is not enabled' warning");
            Assert.That(await _profilePage.IsEnable2FAButtonVisibleAsync(), Is.True,
                "Enable 2FA button should be visible");
            Assert.That(await _profilePage.IsReset2FAButtonVisibleAsync(), Is.False,
                "Reset 2FA button should NOT be visible when TOTP is disabled");
        }
    }

    [Test]
    public async Task Profile_Totp_ShowsEnabledState_WhenEnabled()
    {
        // Arrange - create user with TOTP enabled and login via cookie injection
        // Cookie-based login bypasses TOTP verification entirely
        var totpUser = await new TestUserBuilder(SharedFactory.Services)
            .AsOwner()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .BuildAsync();

        await LoginAsAsync(totpUser);

        // Act - navigate to profile page
        await _profilePage.NavigateAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert - TOTP section shows enabled state
            Assert.That(await _profilePage.IsTotpEnabledAsync(), Is.True,
                "Should show '2FA is currently enabled' alert");
            Assert.That(await _profilePage.IsReset2FAButtonVisibleAsync(), Is.True,
                "Reset 2FA button should be visible");
            Assert.That(await _profilePage.IsEnable2FAButtonVisibleAsync(), Is.False,
                "Enable 2FA button should NOT be visible when TOTP is enabled");
        }
    }

    [Test]
    public async Task Profile_Totp_EnableFlow_OpensDialog()
    {
        // Arrange - login as Owner (TOTP disabled)
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();

        // Act - click Enable 2FA button
        await _profilePage.ClickEnable2FAButtonAsync();

        // Wait for dialog to appear using web-first assertion
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();

        using (Assert.EnterMultipleScope())
        {
            // Assert - dialog opens with expected elements
            Assert.That(await _profilePage.IsTotpSetupDialogVisibleAsync(), Is.True,
                "TOTP setup dialog should be visible");
            Assert.That(await _profilePage.IsTotpQRCodeVisibleAsync(), Is.True,
                "QR code should be visible in the dialog");
            Assert.That(await _profilePage.IsTotpManualKeyVisibleAsync(), Is.True,
                "Manual entry key section should be visible");
        }

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

        using (Assert.EnterMultipleScope())
        {
            // Assert - shows no accounts message
            Assert.That(await _profilePage.IsNoLinkedAccountsMessageVisibleAsync(), Is.True,
                "Should show 'No Telegram accounts linked' message");
            Assert.That(await _profilePage.IsLinkedAccountsTableVisibleAsync(), Is.False,
                "Linked accounts table should NOT be visible when no accounts are linked");
        }
    }

    [Test]
    public async Task Profile_TelegramLinking_GenerateToken()
    {
        // Arrange - login as Owner
        await LoginAsOwnerAsync();
        await _profilePage.NavigateAsync();

        // Act - click Link New Telegram Account
        await _profilePage.ClickLinkNewAccountButtonAsync();

        // Wait for the token alert to appear (async token generation)
        var tokenAlert = Page.Locator(".mud-alert:has-text('Your Link Token')");
        await Expect(tokenAlert).ToBeVisibleAsync(new() { Timeout = 5000 });

        // Assert - token is generated and displayed
        Assert.That(await _profilePage.IsLinkTokenVisibleAsync(), Is.True,
            "Link token should be visible after clicking the button");

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

        using (Assert.EnterMultipleScope())
        {
            // Assert - linked accounts table is visible
            Assert.That(await _profilePage.IsLinkedAccountsTableVisibleAsync(), Is.True,
                "Linked accounts table should be visible");
            Assert.That(await _profilePage.IsNoLinkedAccountsMessageVisibleAsync(), Is.False,
                "No accounts message should NOT be visible when accounts are linked");
        }

        // Verify the linked account details
        var count = await _profilePage.GetLinkedAccountsCountAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(1),
                      "Should have exactly 1 linked account");

            Assert.That(await _profilePage.HasLinkedAccountWithUsernameAsync("@testlinkeduser"), Is.True,
                "Should show the linked account's username");
        }
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

        // Verify account is initially linked
        Assert.That(await _profilePage.GetLinkedAccountsCountAsync(), Is.EqualTo(1),
            "Should have 1 linked account before unlinking");

        // Act - click Unlink button
        await _profilePage.ClickUnlinkButtonAsync(0);

        // Assert - account is unlinked
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(snackbar, Does.Contain("unlinked successfully"),
                      "Should show unlink success message");

            // Verify account is removed from the table
            Assert.That(await _profilePage.IsNoLinkedAccountsMessageVisibleAsync(), Is.True,
                "Should show no accounts message after unlinking");
        }
    }

    #endregion
}
