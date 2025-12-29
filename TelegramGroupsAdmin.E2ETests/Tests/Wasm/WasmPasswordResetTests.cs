using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Ui.Server.Services.Email;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// Tests for the password reset flow in the WASM UI.
/// Flow: /forgot-password → email sent → /reset-password?token=xxx → /login
/// Uses WasmSharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmPasswordResetTests : WasmSharedE2ETestBase
{
    private ForgotPasswordPage _forgotPage = null!;
    private ResetPasswordPage _resetPage = null!;
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetUp()
    {
        _forgotPage = new ForgotPasswordPage(Page);
        _resetPage = new ResetPasswordPage(Page);
        _loginPage = new LoginPage(Page);
    }

    [Test]
    public async Task ForgotPassword_WithValidEmail_ShowsSuccessMessage()
    {
        // Arrange - create a verified user
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-reset"))
            .WithPassword(TestCredentials.GeneratePassword())
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Act - request password reset
        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);

        // Assert - success message shown
        await _forgotPage.WaitForSuccessAsync();
        Assert.That(await _forgotPage.HasSuccessMessageAsync(), Is.True,
            "Should show success message after requesting reset");
    }

    [Test]
    public async Task ForgotPassword_WithNonexistentEmail_StillShowsSuccessMessage()
    {
        // Arrange - no user exists with this email

        // Act - request reset for nonexistent email
        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync("nonexistent@e2e.local");

        // Assert - still shows success (security: don't reveal if email exists)
        await _forgotPage.WaitForSuccessAsync();
        Assert.That(await _forgotPage.HasSuccessMessageAsync(), Is.True,
            "Should show success even for nonexistent emails (security)");
    }

    [Test]
    public async Task ForgotPassword_WithEmptyEmail_ShowsError()
    {
        // Act - submit with empty email
        await _forgotPage.NavigateAsync();
        await _forgotPage.SubmitAsync();

        // Assert - should show error
        Assert.That(await _forgotPage.HasErrorMessageAsync(), Is.True,
            "Should show error for empty email");
    }

    [Test]
    public async Task ForgotPassword_SendsResetEmail()
    {
        // Arrange - create a verified user
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-email-sent"))
            .WithPassword(TestCredentials.GeneratePassword())
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Act - request password reset
        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);
        await _forgotPage.WaitForSuccessAsync();

        // Assert - email was sent with reset link
        var emails = EmailService.GetEmailsByTemplate<EmailTemplateData.PasswordReset>().ToList();
        Assert.That(emails, Has.Count.EqualTo(1), "Should send exactly one reset email");

        var email = emails[0];
        Assert.That(email.To, Does.Contain(user.Email), "Email should be sent to correct address");
        var templateData = email.TemplateData as EmailTemplateData.PasswordReset;
        Assert.That(templateData?.ResetLink, Is.Not.Null.And.Not.Empty, "Email should contain reset link");
    }

    [Test]
    public async Task ResetPassword_WithValidToken_CompletesReset()
    {
        // Arrange - create user and request password reset
        var originalPassword = TestCredentials.GeneratePassword();
        var newPassword = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-valid-reset"))
            .WithPassword(originalPassword)
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Request reset and get token from email
        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);
        await _forgotPage.WaitForSuccessAsync();

        var resetLink = GetResetLinkFromEmail(user.Email);
        Assert.That(resetLink, Is.Not.Null, "Reset link should be in email");

        // Act - navigate to reset page and set new password
        await _resetPage.NavigateFromLinkAsync(resetLink!);
        await _resetPage.WaitForFormAsync();
        await _resetPage.ResetPasswordAsync(newPassword);

        // Assert - success message shown
        await _resetPage.WaitForSuccessAsync();
        Assert.That(await _resetPage.HasSuccessMessageAsync(), Is.True,
            "Should show success after password reset");

        // Wait for redirect to login
        await _resetPage.WaitForRedirectToLoginAsync();

        // Verify can login with new password
        await _loginPage.LoginAsync(user.Email, newPassword);
        await _loginPage.WaitForRedirectAsync();

        // Should no longer be on login page
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task ResetPassword_WithMismatchedPasswords_ShowsError()
    {
        // Arrange - create user and get reset token
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-mismatch"))
            .WithPassword(TestCredentials.GeneratePassword())
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);
        await _forgotPage.WaitForSuccessAsync();

        var resetLink = GetResetLinkFromEmail(user.Email);
        Assert.That(resetLink, Is.Not.Null);

        // Act - try to reset with mismatched passwords
        await _resetPage.NavigateFromLinkAsync(resetLink!);
        await _resetPage.WaitForFormAsync();
        await _resetPage.ResetPasswordAsync("NewPassword123!", "DifferentPassword456!");

        // Assert - should show error
        var errorMessage = await _resetPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Does.Contain("match").IgnoreCase,
            "Should show password mismatch error");
    }

    [Test]
    public async Task ResetPassword_WithShortPassword_ShowsError()
    {
        // Arrange - create user and get reset token
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-short-pw"))
            .WithPassword(TestCredentials.GeneratePassword())
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);
        await _forgotPage.WaitForSuccessAsync();

        var resetLink = GetResetLinkFromEmail(user.Email);
        Assert.That(resetLink, Is.Not.Null);

        // Act - try to reset with too short password
        await _resetPage.NavigateFromLinkAsync(resetLink!);
        await _resetPage.WaitForFormAsync();
        await _resetPage.ResetPasswordAsync("short"); // Less than 8 chars

        // Assert - should show error
        var errorMessage = await _resetPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Does.Contain("8").Or.Contain("characters").IgnoreCase,
            "Should show minimum length error");
    }

    [Test]
    public async Task ResetPassword_WithInvalidToken_ShowsErrorOnSubmit()
    {
        // Arrange - navigate with invalid token (form still shows until submit)
        await _resetPage.NavigateAsync("invalid-token-12345");
        await _resetPage.WaitForFormAsync();

        // Act - try to submit with the invalid token
        await _resetPage.ResetPasswordAsync("ValidPassword123!");

        // Assert - should show error after submission
        var errorMessage = await _resetPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null, "Should show error for invalid token");
        Assert.That(errorMessage, Does.Contain("expired").Or.Contain("invalid").Or.Contain("failed").IgnoreCase,
            "Error message should indicate token issue");
    }

    [Test]
    public async Task ResetPassword_WithNoToken_ShowsError()
    {
        // Act - navigate without token
        await Page.GotoAsync("/reset-password");
        await _resetPage.WaitForPageAsync();

        // Assert - should show error (use Expect auto-retry for WASM timing)
        await Expect(Page.Locator(".mud-alert-text-error")).ToBeVisibleAsync();
        await Expect(Page.Locator("a[href='/forgot-password']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task ForgotPassword_PageTitle_ShowsCorrectHeading()
    {
        // Act
        await _forgotPage.NavigateAsync();
        await _forgotPage.WaitForPageAsync();

        // Assert
        var title = await _forgotPage.GetPageTitleAsync();
        Assert.That(title, Does.Contain("Reset").Or.Contain("Password").IgnoreCase,
            "Page title should indicate password reset");
    }

    [Test]
    public async Task ResetPassword_OldPasswordNoLongerWorks()
    {
        // Arrange - create user with known password
        var originalPassword = TestCredentials.GeneratePassword();
        var newPassword = TestCredentials.GeneratePassword();
        var user = await new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail("wasm-old-pw-invalid"))
            .WithPassword(originalPassword)
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Request reset and complete it
        await _forgotPage.NavigateAsync();
        await _forgotPage.RequestResetAsync(user.Email);
        await _forgotPage.WaitForSuccessAsync();

        var resetLink = GetResetLinkFromEmail(user.Email);
        await _resetPage.NavigateFromLinkAsync(resetLink!);
        await _resetPage.WaitForFormAsync();
        await _resetPage.ResetPasswordAsync(newPassword);
        await _resetPage.WaitForRedirectToLoginAsync();

        // Act - try to login with old password
        await _loginPage.LoginAsync(user.Email, originalPassword);

        // Assert - should fail
        await Expect(Page.Locator(".alert-error")).ToBeVisibleAsync();
        Assert.That(await _loginPage.HasErrorMessageAsync(), Is.True,
            "Old password should no longer work after reset");
    }

    /// <summary>
    /// Extracts the reset link from a captured email.
    /// </summary>
    private string? GetResetLinkFromEmail(string email)
    {
        var emails = EmailService.GetEmailsTo(email)
            .Where(e => e.TemplateData is EmailTemplateData.PasswordReset)
            .ToList();

        if (emails.Count == 0)
            return null;

        return (emails[0].TemplateData as EmailTemplateData.PasswordReset)?.ResetLink;
    }
}
