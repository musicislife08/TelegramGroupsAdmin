using System.Text.RegularExpressions;
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
        await Expect(_profilePage.PageTitle).ToBeVisibleAsync();
        await Expect(_profilePage.PageTitle).ToHaveTextAsync("Profile Settings");

        // Verify all sections are visible
        await Expect(_profilePage.AccountInfoSection).ToBeVisibleAsync();
        await Expect(_profilePage.ChangePasswordSection).ToBeVisibleAsync();
        await Expect(_profilePage.TotpSection).ToBeVisibleAsync();
        await Expect(_profilePage.TelegramLinkingSection).ToBeVisibleAsync();

        // Verify account info fields are populated
        await _profilePage.AssertAccountInfoFieldsVisibleAsync();
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
        await Expect(_profilePage.Snackbar).ToContainTextAsync("Please fill in all fields");
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
        await Expect(_profilePage.Snackbar).ToContainTextAsync("New passwords do not match");
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
        await Expect(_profilePage.Snackbar).ToContainTextAsync("at least 8 characters");
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
        await Expect(_profilePage.Snackbar).ToContainTextAsync("Current password is incorrect");
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
        await Expect(_profilePage.Snackbar).ToContainTextAsync("Password changed successfully");

        // Verify form fields are cleared
        await Expect(_profilePage.CurrentPasswordInput).ToHaveValueAsync("");
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

        // Assert - TOTP section shows disabled state. The positive checks come first so the
        // absence check cannot pass before the TOTP section has rendered its state.
        await Expect(_profilePage.TotpDisabledAlert).ToBeVisibleAsync();
        await Expect(_profilePage.Enable2FAButton).ToBeVisibleAsync();
        await Expect(_profilePage.Reset2FAButton).Not.ToBeVisibleAsync();
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

        // Assert - TOTP section shows enabled state. The positive checks come first so the
        // absence check cannot pass before the TOTP section has rendered its state.
        await Expect(_profilePage.TotpEnabledAlert).ToBeVisibleAsync();
        await Expect(_profilePage.Reset2FAButton).ToBeVisibleAsync();
        await Expect(_profilePage.Enable2FAButton).Not.ToBeVisibleAsync();
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

        // Assert - dialog opens with expected elements
        await Expect(_profilePage.TotpSetupDialog).ToBeVisibleAsync();
        await Expect(_profilePage.TotpQrCode).ToBeVisibleAsync();
        await Expect(_profilePage.TotpManualKeyText).ToBeVisibleAsync();

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

        // Assert - shows no accounts message (positive check first, so the table absence
        // check below cannot pass before the section has rendered)
        await Expect(_profilePage.NoLinkedAccountsMessage).ToBeVisibleAsync();
        await Expect(_profilePage.LinkedAccountsTable).Not.ToBeVisibleAsync();
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
        await Expect(_profilePage.LinkTokenAlert).ToBeVisibleAsync();

        // Generated token should be non-empty and exactly 12 characters
        await Expect(_profilePage.LinkTokenInput).ToHaveValueAsync(new Regex("^.{12}$"));

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

        // Assert - linked accounts table is visible (positive check first, so the message
        // absence check below cannot pass before the section has rendered)
        await Expect(_profilePage.LinkedAccountsTable).ToBeVisibleAsync();
        await Expect(_profilePage.NoLinkedAccountsMessage).Not.ToBeVisibleAsync();

        // Verify the linked account details
        await Expect(_profilePage.LinkedAccountRows).ToHaveCountAsync(1);
        await Expect(_profilePage.LinkedAccountUsernameCell("@testlinkeduser")).ToBeVisibleAsync();
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
        await Expect(_profilePage.LinkedAccountRows).ToHaveCountAsync(1);

        // Act - click Unlink button
        await _profilePage.ClickUnlinkButtonAsync(0);

        // Assert - account is unlinked
        await Expect(_profilePage.Snackbar).ToContainTextAsync("unlinked successfully");

        // Verify account is removed from the table
        await Expect(_profilePage.NoLinkedAccountsMessage).ToBeVisibleAsync();
    }

    #endregion
}
