using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Ui.Server.Services.Email;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// Tests for user registration flow in the WASM UI.
/// Uses WasmSharedE2ETestBase for faster test execution with shared factory.
///
/// IMPORTANT: First-run (owner) registration can NEVER require email verification because:
/// 1. Email settings can only be configured by a logged-in admin
/// 2. No admin exists until first user registers
/// 3. Therefore, first-run owner is ALWAYS auto-verified
///
/// Email verification only applies to SUBSEQUENT users who register via invite codes,
/// after an admin has configured email settings.
/// </summary>
[TestFixture]
public class WasmRegistrationTests : WasmSharedE2ETestBase
{
    private RegisterPage _registerPage = null!;
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetUp()
    {
        _registerPage = new RegisterPage(Page);
        _loginPage = new LoginPage(Page);
    }

    [Test]
    public async Task Registration_FirstRun_CreatesOwnerAndRedirectsToTotpSetup()
    {
        // Arrange - first run means no users exist
        // Email verification is impossible because settings can't be configured yet
        // TOTP setup is required for all new accounts by default
        var email = TestCredentials.GenerateEmail("wasm-owner");
        var password = TestCredentials.GeneratePassword();

        // Act - Register the first (owner) account
        await _registerPage.NavigateAsync();

        // Verify we're in first-run mode
        Assert.That(await _registerPage.IsFirstRunModeAsync(), Is.True,
            "Should show 'Setup Owner Account' for first user");

        // First-run should NOT show invite code field
        Assert.That(await _registerPage.IsInviteCodeVisibleAsync(), Is.False,
            "First-run registration should not require invite code");

        await _registerPage.RegisterAsync(email, password);

        // Registration succeeds and auto-redirects to login page
        // The success message is brief (2s) before redirect, so we verify the redirect instead
        await Page.WaitForURLAsync("**/login", new() { Timeout = 10000 });

        // Login with credentials - should redirect to TOTP setup (TotpEnabled=true by default)
        await _loginPage.LoginAsync(email, password);

        // Wait for redirect to TOTP setup page (can't use WaitForRedirectAsync because
        // /login/setup-2fa still contains "/login")
        await Page.WaitForURLAsync("**/login/setup-2fa**", new() { Timeout = 10000 });

        // Assert - should be on TOTP setup page (not logged in yet)
        Assert.That(Page.Url, Does.Contain("/login/setup-2fa"),
            "New owner should be redirected to TOTP setup after login");

        // Verify TOTP setup page elements are present
        var setupTitle = Page.Locator("h1:has-text('Two-Factor Authentication')");
        Assert.That(await setupTitle.IsVisibleAsync(), Is.True,
            "Should show TOTP setup page title");

        // Verify no verification emails were sent (owner is auto-verified)
        var verificationEmails = EmailService.GetEmailsByTemplate<EmailTemplateData.EmailVerification>().ToList();
        Assert.That(verificationEmails, Is.Empty,
            "First-run owner should not receive verification email (auto-verified)");
    }

    [Test]
    public async Task Registration_FirstRun_ShowsRestoreBackupOption()
    {
        // Arrange & Act - Navigate to registration
        await _registerPage.NavigateAsync();

        // Assert - First-run mode should offer backup restore option
        Assert.That(await _registerPage.IsFirstRunModeAsync(), Is.True,
            "Should be in first-run mode");
        Assert.That(await _registerPage.IsRestoreBackupAvailableAsync(), Is.True,
            "First-run should show 'Restore from Backup' option");
    }

    [Test]
    public async Task Registration_WithWeakPassword_ShowsError()
    {
        // Arrange
        var email = TestCredentials.GenerateEmail("wasm-weak");

        // Act - try to register with weak password
        await _registerPage.NavigateAsync();
        await _registerPage.FillEmailAsync(email);
        await _registerPage.FillPasswordAsync("weak");
        await _registerPage.FillConfirmPasswordAsync("weak");

        // Assert - MudBlazor validation shows errors inline without form submission
        // Validation runs on blur/immediate mode, error appears in helper text
        var passwordError = Page.Locator(".mud-input-helper-text:has-text('Password must be at least 8 characters')");
        await passwordError.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        Assert.That(await passwordError.IsVisibleAsync(), Is.True,
            "Should show validation error for weak password");
    }

    [Test]
    public async Task Registration_WithMismatchedPasswords_ShowsError()
    {
        // Arrange
        var email = TestCredentials.GenerateEmail("wasm-mismatch");
        var password = TestCredentials.GeneratePassword();

        // Act - register with mismatched passwords
        await _registerPage.NavigateAsync();
        await _registerPage.FillEmailAsync(email);
        await _registerPage.FillPasswordAsync(password);
        await _registerPage.FillConfirmPasswordAsync("DifferentPassword123!");

        // Assert - MudBlazor validation shows errors inline without form submission
        // Validation runs on blur/immediate mode, error appears in helper text
        var mismatchError = Page.Locator(".mud-input-helper-text:has-text('Passwords do not match')");
        await mismatchError.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });

        Assert.That(await mismatchError.IsVisibleAsync(), Is.True,
            "Should show validation error for mismatched passwords");
    }

    // TODO: Add test for invited user registration with email verification
    // This requires:
    // 1. Create owner account (first-run)
    // 2. Log in as owner
    // 3. Configure email settings via Settings UI
    // 4. Create an invite
    // 5. Register new user with invite code
    // 6. Verify email verification flow works for invited user
}
