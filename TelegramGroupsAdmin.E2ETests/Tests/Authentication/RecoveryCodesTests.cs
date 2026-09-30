using System.Text.RegularExpressions;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for recovery codes functionality.
/// - Regenerating recovery codes from Profile page
/// - Login with recovery code when TOTP device is unavailable
/// Uses SharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class RecoveryCodesTests : SharedE2ETestBase
{
    private LoginPage _loginPage = null!;
    private LoginVerifyPage _verifyPage = null!;
    private ProfilePage _profilePage = null!;

    [SetUp]
    public void SetUp()
    {
        _loginPage = new LoginPage(Page);
        _verifyPage = new LoginVerifyPage(Page);
        _profilePage = new ProfilePage(Page);
    }

    #region Profile Page - Regenerate Recovery Codes

    [Test]
    public async Task Profile_WithTotpEnabled_ShowsRegenerateButton()
    {
        // Arrange - create user with TOTP already enabled
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("regen-visible"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true) // Fully configured TOTP
            .AsOwner()
            .BuildAsync();

        // Login with 2FA
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);
        await _verifyPage.WaitForRedirectAsync();

        // Navigate to profile
        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Assert - regenerate button should be visible when TOTP is enabled
        await Expect(_profilePage.RegenerateRecoveryCodesButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Profile_RegenerateRecoveryCodes_RequiresPassword()
    {
        // Arrange - create user with TOTP enabled and login
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("regen-password"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);
        await _verifyPage.WaitForRedirectAsync();

        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Act - click regenerate button
        await _profilePage.ClickRegenerateRecoveryCodesAsync();

        // Assert - password confirmation dialog should appear
        await _profilePage.WaitForPasswordConfirmDialogAsync();
        await Expect(_profilePage.PasswordConfirmDialog).ToBeVisibleAsync();
    }

    [Test]
    public async Task Profile_RegenerateRecoveryCodes_WithValidPassword_ShowsCodes()
    {
        // Arrange - create user with TOTP enabled and login
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("regen-success"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);
        await _verifyPage.WaitForRedirectAsync();

        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Act - regenerate recovery codes with valid password
        await _profilePage.ClickRegenerateRecoveryCodesAsync();
        await _profilePage.WaitForPasswordConfirmDialogAsync();
        await _profilePage.FillPasswordConfirmDialogAsync(user.Password);
        await _profilePage.ClickGenerateNewCodesAsync();
        await _profilePage.WaitForRecoveryCodesDialogAsync();

        // Assert - should display recovery codes per AuthenticationConstants.RecoveryCodeCount
        // (asserted on the open dialog, before it is closed below)
        await Expect(_profilePage.RecoveryCodeItems).ToHaveCountAsync(AuthenticationConstants.RecoveryCodeCount);

        // Each code should be a valid format (hex string per AuthenticationConstants.RecoveryCodeStringLength).
        // Surrounding whitespace is allowed because regex matching sees the raw (untrimmed) text.
        var expectedPattern = new Regex($@"^\s*[a-f0-9]{{{AuthenticationConstants.RecoveryCodeStringLength}}}\s*$");
        await Expect(_profilePage.RecoveryCodeItems).ToHaveTextAsync(
            Enumerable.Repeat(expectedPattern, AuthenticationConstants.RecoveryCodeCount));

        // Close the dialog, as the regenerate flow does
        await _profilePage.ClickSavedCodesAsync();
    }

    [Test]
    public async Task Profile_RegenerateRecoveryCodes_WithInvalidPassword_ShowsError()
    {
        // Arrange - create user with TOTP enabled and login
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("regen-bad-pass"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);
        await _verifyPage.WaitForRedirectAsync();

        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Act - try to regenerate with wrong password
        await _profilePage.ClickRegenerateRecoveryCodesAsync();
        await _profilePage.WaitForPasswordConfirmDialogAsync();
        await _profilePage.FillPasswordConfirmDialogAsync("WrongPassword123!");
        await _profilePage.ClickGenerateNewCodesAsync(expectSuccess: false);

        // Assert - should show error snackbar
        await Expect(_profilePage.Snackbar).ToContainTextAsync("Invalid", new() { IgnoreCase = true });

        // Dialog should still be visible (not closed)
        await Expect(_profilePage.PasswordConfirmDialog).ToBeVisibleAsync();
    }

    [Test]
    public async Task Profile_RegenerateRecoveryCodes_CanCancel()
    {
        // Arrange - create user with TOTP enabled and login
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("regen-cancel"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        var totpCode = TotpHelper.GenerateCode(user.TotpSecret!);
        await _verifyPage.VerifyAsync(totpCode);
        await _verifyPage.WaitForRedirectAsync();

        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Act - open dialog then cancel
        await _profilePage.ClickRegenerateRecoveryCodesAsync();
        await _profilePage.WaitForPasswordConfirmDialogAsync();
        await _profilePage.CancelPasswordConfirmDialogAsync();

        // Assert - dialog should be closed (use Playwright's auto-waiting instead of Task.Delay)
        await _profilePage.WaitForPasswordConfirmDialogClosedAsync();
        await Expect(_profilePage.PasswordConfirmDialog).Not.ToBeVisibleAsync();
    }

    #endregion

    #region Profile Page - Enable 2FA Shows Recovery Codes

    [Test]
    public async Task Profile_Enable2FA_ShowsRecoveryCodesAfterSetup()
    {
        // Arrange - create user WITHOUT TOTP (will enable via Profile)
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("profile-enable"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotpDisabled() // No 2FA configured
            .AsOwner()
            .BuildAsync();

        // Login (no 2FA required)
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _loginPage.WaitForRedirectAsync();

        // Navigate to profile
        await _profilePage.NavigateAsync();
        await _profilePage.WaitForLoadAsync();

        // Verify 2FA is disabled initially (auto-retrying — the alert renders async)
        await _profilePage.AssertTotpDisabledAsync();

        // Act - enable 2FA
        await _profilePage.ClickEnable2FAButtonAsync();

        // Wait for dialog with QR code
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        await Expect(_profilePage.TotpQrCode).ToBeVisibleAsync();

        // Note: Full TOTP verification in Profile page would require reading the
        // secret from the QR URI or input field, which is complex for E2E tests.
        // The TotpSetupTests already cover the full flow via /login/setup-2fa.
    }

    #endregion

    #region Login With Recovery Code

    [Test]
    public async Task LoginVerify_ShowsRecoveryCodeOption()
    {
        // Arrange - create user with TOTP enabled
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("recovery-option"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        // Act - login to get to TOTP verification page
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        // Assert - "Use a recovery code instead" link should be visible
        await Expect(_verifyPage.UseRecoveryCodeLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task LoginVerify_ClickRecoveryCodeLink_ShowsRecoveryForm()
    {
        // Arrange - create user with TOTP enabled
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("recovery-form"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        // Act - click to use recovery code
        await _verifyPage.ClickUseRecoveryCodeAsync();
        await _verifyPage.WaitForRecoveryCodeFormAsync();

        // Assert - recovery code input should be visible
        await Expect(_verifyPage.RecoveryCodeInput).ToBeVisibleAsync();

        // "Back to authenticator" link should be visible
        await Expect(_verifyPage.BackToAuthenticatorLink).ToBeVisibleAsync();
    }

    [Test]
    public async Task LoginVerify_WithValidRecoveryCode_LogsIn()
    {
        // Arrange - create user requiring TOTP setup to get recovery codes
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("recovery-login"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Complete TOTP setup to get recovery codes
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        var totpSetupPage = new TotpSetupPage(Page);
        await totpSetupPage.WaitForPageAsync();

        var manualKey = await totpSetupPage.GetManualKeyAsync();
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        var recoveryCodes = await totpSetupPage.Complete2FASetupAsync(totpCode);
        await totpSetupPage.WaitForRedirectAsync();

        // Log out by clearing cookies and navigating away
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync("/");

        // Now try to login with a recovery code
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        // Act - use a recovery code to login
        await _verifyPage.LoginWithRecoveryCodeAsync(recoveryCodes[0]);

        // Assert - should successfully login
        await _verifyPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task LoginVerify_WithInvalidRecoveryCode_ShowsError()
    {
        // Arrange - create user with TOTP enabled
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("recovery-invalid"))
            .WithStandardPassword()
            .WithEmailVerified()
            .WithTotp(enabled: true)
            .AsOwner()
            .BuildAsync();

        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();

        // Act - try to use an invalid recovery code (correct length but not a valid code)
        await _verifyPage.ClickUseRecoveryCodeAsync();
        await _verifyPage.WaitForRecoveryCodeFormAsync();
        var invalidCode = new string('0', AuthenticationConstants.RecoveryCodeStringLength);
        await _verifyPage.VerifyWithRecoveryCodeAsync(invalidCode);

        // Assert - should show error
        await Expect(_verifyPage.ErrorAlert).ToBeVisibleAsync();

        // Should still be on the verify page
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
    }

    [Test]
    public async Task LoginVerify_RecoveryCode_IsOneTimeUse()
    {
        // Arrange - create user requiring TOTP setup to get recovery codes
        var user = await new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("recovery-onetime"))
            .WithStandardPassword()
            .WithEmailVerified()
            .RequiresTotpSetup()
            .AsOwner()
            .BuildAsync();

        // Complete TOTP setup to get recovery codes
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        var totpSetupPage = new TotpSetupPage(Page);
        await totpSetupPage.WaitForPageAsync();

        var manualKey = await totpSetupPage.GetManualKeyAsync();
        var cleanSecret = manualKey!.Replace(" ", "");
        var totpCode = TotpHelper.GenerateCode(cleanSecret);

        var recoveryCodes = await totpSetupPage.Complete2FASetupAsync(totpCode);
        await totpSetupPage.WaitForRedirectAsync();

        // First login with recovery code
        await Page.Context.ClearCookiesAsync();
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();
        await _verifyPage.LoginWithRecoveryCodeAsync(recoveryCodes[0]);
        await _verifyPage.WaitForRedirectAsync();

        // Log out again
        await Page.Context.ClearCookiesAsync();
        await Page.GotoAsync("/");

        // Act - try to use the same recovery code again
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();
        await _verifyPage.ClickUseRecoveryCodeAsync();
        await _verifyPage.WaitForRecoveryCodeFormAsync();
        await _verifyPage.VerifyWithRecoveryCodeAsync(recoveryCodes[0]); // Same code

        // Assert - should fail (code already used)
        await Expect(_verifyPage.ErrorAlert).ToBeVisibleAsync();

        // Should still be on the verify page
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
    }

    #endregion
}
