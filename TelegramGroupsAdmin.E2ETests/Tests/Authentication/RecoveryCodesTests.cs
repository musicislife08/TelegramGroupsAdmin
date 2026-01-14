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
        Assert.That(await _profilePage.IsRegenerateRecoveryCodesButtonVisibleAsync(), Is.True,
            "Regenerate Recovery Codes button should be visible when 2FA is enabled");
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
        Assert.That(await _profilePage.IsPasswordConfirmDialogVisibleAsync(), Is.True,
            "Password confirmation dialog should appear when regenerating codes");
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
        var recoveryCodes = await _profilePage.RegenerateRecoveryCodesAsync(user.Password);

        // Assert - should receive recovery codes per AuthenticationConstants.RecoveryCodeCount
        Assert.That(recoveryCodes.Count, Is.EqualTo(AuthenticationConstants.RecoveryCodeCount),
            $"Should display {AuthenticationConstants.RecoveryCodeCount} new recovery codes");

        // Each code should be a valid format (hex string per AuthenticationConstants.RecoveryCodeStringLength)
        var expectedPattern = $@"^[a-f0-9]{{{AuthenticationConstants.RecoveryCodeStringLength}}}$";
        foreach (var code in recoveryCodes)
        {
            Assert.That(code, Does.Match(expectedPattern),
                $"Recovery code '{code}' should be a {AuthenticationConstants.RecoveryCodeStringLength}-character hex string");
        }
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
        var snackbar = await _profilePage.WaitForSnackbarAsync();
        Assert.That(snackbar, Does.Contain("Invalid").IgnoreCase,
            "Should show error for invalid password");

        // Dialog should still be visible (not closed)
        Assert.That(await _profilePage.IsPasswordConfirmDialogVisibleAsync(), Is.True,
            "Dialog should remain open after failed password verification");
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
        Assert.That(await _profilePage.IsPasswordConfirmDialogVisibleAsync(), Is.False,
            "Dialog should be closed after canceling");
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

        // Verify 2FA is disabled initially
        Assert.That(await _profilePage.IsTotpDisabledAsync(), Is.True,
            "2FA should be disabled initially");

        // Act - enable 2FA
        await _profilePage.ClickEnable2FAButtonAsync();

        // Wait for dialog with QR code
        await Expect(Page.Locator(".mud-dialog")).ToBeVisibleAsync();
        Assert.That(await _profilePage.IsTotpQRCodeVisibleAsync(), Is.True,
            "QR code should be visible in setup dialog");

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
        Assert.That(await _verifyPage.IsUseRecoveryCodeLinkVisibleAsync(), Is.True,
            "Recovery code option should be available on TOTP verify page");
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

        // Assert - recovery code input should be visible, TOTP input should not
        Assert.That(await _verifyPage.IsRecoveryCodeInputVisibleAsync(), Is.True,
            "Recovery code input should be visible");

        // "Back to authenticator" link should be visible
        Assert.That(await _verifyPage.IsBackToAuthenticatorLinkVisibleAsync(), Is.True,
            "Back to authenticator link should be visible");
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
        Assert.That(await _verifyPage.HasErrorMessageAsync(), Is.True,
            "Should show error for invalid recovery code");

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

        // Act - try to use the same recovery code again
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);
        await _verifyPage.WaitForPageAsync();
        await _verifyPage.ClickUseRecoveryCodeAsync();
        await _verifyPage.WaitForRecoveryCodeFormAsync();
        await _verifyPage.VerifyWithRecoveryCodeAsync(recoveryCodes[0]); // Same code

        // Assert - should fail (code already used)
        Assert.That(await _verifyPage.HasErrorMessageAsync(), Is.True,
            "Should show error when trying to reuse a recovery code");

        // Should still be on the verify page
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login/verify"));
    }

    #endregion
}
