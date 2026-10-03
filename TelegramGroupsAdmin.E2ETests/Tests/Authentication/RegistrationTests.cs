using System.Text.RegularExpressions;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Services.Email;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for user registration flow.
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
public class RegistrationTests : E2ETestBase
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
        var email = TestCredentials.GenerateEmail("owner");
        var password = TestCredentials.GeneratePassword();

        // Act - Register the first (owner) account
        await _registerPage.NavigateAsync();

        // Verify we're in first-run mode. This positive check also syncs on the first-run
        // re-render, so the invite-code absence check below cannot pass against the initial render.
        await Expect(_registerPage.FirstRunTitle).ToBeVisibleAsync();

        // First-run should NOT show invite code field
        await Expect(_registerPage.InviteCodeField).Not.ToBeVisibleAsync();

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
        await Expect(Page).ToHaveURLAsync(new Regex("/login/setup-2fa"));

        // Verify TOTP setup page elements are present
        var setupTitle = Page.Locator("h1:has-text('Two-Factor Authentication')");
        await Expect(setupTitle).ToBeVisibleAsync();

        // Verify no verification emails were sent (owner is auto-verified)
        var verificationEmails = EmailService.GetEmailsByTemplate(EmailTemplate.EmailVerification).ToList();
        Assert.That(verificationEmails, Is.Empty,
            "First-run owner should not receive verification email (auto-verified)");
    }

    [Test]
    public async Task Registration_FirstRun_ShowsRestoreBackupOption()
    {
        // Arrange & Act - Navigate to registration
        await _registerPage.NavigateAsync();

        // Assert - First-run mode should offer backup restore option
        await Expect(_registerPage.FirstRunTitle).ToBeVisibleAsync();
        await Expect(_registerPage.RestoreBackupButton).ToBeVisibleAsync();
    }

    [Test]
    public async Task Registration_WithWeakPassword_ShowsError()
    {
        // Arrange
        var email = TestCredentials.GenerateEmail("weak");

        // Act - try to register with weak password
        await _registerPage.NavigateAsync();
        await _registerPage.FillEmailAsync(email);
        await _registerPage.FillPasswordAsync("weak");
        await _registerPage.FillConfirmPasswordAsync("weak");

        // Assert - MudBlazor validation shows errors inline without form submission
        // Validation runs on blur/immediate mode, error appears in helper text
        var passwordError = Page.Locator(".mud-input-helper-text:has-text('Password must be at least 8 characters')");
        await Expect(passwordError).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    [Test]
    public async Task Registration_WithMismatchedPasswords_ShowsError()
    {
        // Arrange
        var email = TestCredentials.GenerateEmail("mismatch");
        var password = TestCredentials.GeneratePassword();

        // Act - register with mismatched passwords
        await _registerPage.NavigateAsync();
        await _registerPage.FillEmailAsync(email);
        await _registerPage.FillPasswordAsync(password);
        await _registerPage.FillConfirmPasswordAsync("DifferentPassword123!");

        // Assert - MudBlazor validation shows errors inline without form submission
        // Validation runs on blur/immediate mode, error appears in helper text
        var mismatchError = Page.Locator(".mud-input-helper-text:has-text('Passwords do not match')");
        await Expect(mismatchError).ToBeVisibleAsync(new() { Timeout = 5000 });
    }

    // Invited-user registration (invite created by the Owner, registration through the generated link,
    // account read back, login) is covered on canonical golden data by
    // Tests/Settings/InviteGoldenTests.RegisterWithInvite_CreatesAnActiveAccountThatLogsIn. Canonical has
    // no SendGrid API key, so that covers the verification-off path; the verification-on path for an
    // invited user needs a config shape canonical does not carry (see the E2E test's class doc).
}
