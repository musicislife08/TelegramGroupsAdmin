using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Email;
using DataModels = TelegramGroupsAdmin.Data.Models;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests for the email verification flow.
/// Flow: Registration → verification email sent → /verify-email?token=xxx → /login
/// Also tests: /resend-verification for users who need a new link.
/// </summary>
[TestFixture]
public class EmailVerificationTests : E2ETestBase
{
    private ResendVerificationPage _resendPage = null!;
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetUp()
    {
        _resendPage = new ResendVerificationPage(Page);
        _loginPage = new LoginPage(Page);
    }

    [Test]
    public async Task VerifyEmail_WithValidToken_VerifiesAndRedirectsToLogin()
    {
        // Arrange - create an unverified user and generate a verification token
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("verify"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        var verificationToken = await CreateVerificationTokenAsync(user.Id, DataModels.TokenType.EmailVerification);

        // Act - navigate to verification link
        await Page.GotoAsync($"/verify-email?token={verificationToken}");

        // Assert - should redirect to login with success indicator
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login\?verified=success"));

        // Verify the success banner is shown on login page
        var successBanner = Page.Locator(".alert-success, .mud-alert-text-success");
        await successBanner.WaitForAsync(new() { State = Microsoft.Playwright.WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.That(await successBanner.IsVisibleAsync(), Is.True,
            "Should show success message on login page after verification");
    }

    [Test]
    public async Task VerifyEmail_WithValidToken_CanLoginAfterVerification()
    {
        // Arrange - create an unverified user with password
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("login-after"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        var verificationToken = await CreateVerificationTokenAsync(user.Id, DataModels.TokenType.EmailVerification);

        // Act - verify email
        await Page.GotoAsync($"/verify-email?token={verificationToken}");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"));

        // Now try to login
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should redirect away from login (successful login)
        await _loginPage.WaitForRedirectAsync();
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"^.*/login$"));
    }

    [Test]
    public async Task VerifyEmail_WithAlreadyVerifiedUser_RedirectsToLoginWithAlreadyVerified()
    {
        // Arrange - create a verified user and generate a token anyway
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("already-verified"))
            .WithStandardPassword()
            .WithEmailVerified(true)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        var verificationToken = await CreateVerificationTokenAsync(user.Id, DataModels.TokenType.EmailVerification);

        // Act - navigate to verification link
        await Page.GotoAsync($"/verify-email?token={verificationToken}");

        // Assert - should redirect to login with "already" indicator
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login\?verified=already"));
    }

    [Test]
    public async Task VerifyEmail_WithInvalidToken_ShowsError()
    {
        // Act - navigate with invalid token
        var response = await Page.GotoAsync("/verify-email?token=invalid-token-12345");

        // Assert - should get a bad request response
        Assert.That(response?.Status, Is.EqualTo(400),
            "Invalid token should return 400 Bad Request");
    }

    [Test]
    public async Task VerifyEmail_WithExpiredToken_ShowsError()
    {
        // Arrange - create user and expired token
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("expired-token"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        // Create an expired token (expired 1 hour ago)
        var expiredToken = await CreateVerificationTokenAsync(
            user.Id,
            DataModels.TokenType.EmailVerification,
            expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        // Act - navigate with expired token
        var response = await Page.GotoAsync($"/verify-email?token={expiredToken}");

        // Assert - should get a bad request response
        Assert.That(response?.Status, Is.EqualTo(400),
            "Expired token should return 400 Bad Request");
    }

    [Test]
    public async Task VerifyEmail_WithNoToken_ShowsError()
    {
        // Act - navigate without token
        var response = await Page.GotoAsync("/verify-email");

        // Assert - should get a bad request response
        Assert.That(response?.Status, Is.EqualTo(400),
            "Missing token should return 400 Bad Request");
    }

    [Test]
    public async Task VerifyEmail_WithUsedToken_ShowsError()
    {
        // Arrange - create user and use the token
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("used-token"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        var verificationToken = await CreateVerificationTokenAsync(user.Id, DataModels.TokenType.EmailVerification);

        // Use the token first time
        await Page.GotoAsync($"/verify-email?token={verificationToken}");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"));

        // Act - try to use the same token again
        var response = await Page.GotoAsync($"/verify-email?token={verificationToken}");

        // Assert - should fail (token already used or user already verified)
        // Either 400 or redirect with "already" indicator
        var url = Page.Url;
        Assert.That(
            response?.Status == 400 || url.Contains("verified=already"),
            Is.True,
            "Used token should either return 400 or indicate already verified");
    }

    [Test]
    public async Task ResendVerification_PageLoads()
    {
        // Act
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();

        // Assert
        var title = await _resendPage.GetPageTitleAsync();
        Assert.That(title, Does.Contain("Verification").Or.Contain("Resend").IgnoreCase,
            "Page title should indicate verification resend");
    }

    [Test]
    public async Task ResendVerification_WithValidUnverifiedEmail_ShowsSuccess()
    {
        // Arrange - create an unverified user and enable email
        await Factory.EnableEmailVerificationAsync();

        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("resend"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        // Act - request resend
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();
        await _resendPage.RequestResendAsync(user.Email);

        // Assert - should show success message
        await _resendPage.WaitForSuccessAsync();
        Assert.That(await _resendPage.HasSuccessMessageAsync(), Is.True,
            "Should show success message after requesting resend");

        // Verify email was actually sent
        var verificationEmails = EmailService.GetEmailsByTemplate(EmailTemplate.EmailVerification).ToList();
        Assert.That(verificationEmails, Has.Count.EqualTo(1), "Should send exactly one verification email");
        Assert.That(verificationEmails[0].To, Does.Contain(user.Email), "Email should be sent to correct address");
    }

    [Test]
    public async Task ResendVerification_WithEmptyEmail_PreventsSubmission()
    {
        // Arrange - navigate to page
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();

        // Act - try to submit with empty email
        await _resendPage.SubmitAsync();

        // Assert - browser's native validation should prevent submission
        // The page should still be on the same form (not showing server-side error)
        // because the HTML5 'required' attribute blocks form submission
        var emailInput = Page.Locator("input#email");
        var isInvalid = await emailInput.EvaluateAsync<bool>("el => !el.validity.valid");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isInvalid, Is.True,
                      "Browser should mark empty required field as invalid");

            // Form should not have been submitted (no success or server error)
            Assert.That(await _resendPage.HasSuccessMessageAsync(), Is.False,
                "Should not show success (form not submitted)");
        }
    }

    [Test]
    public async Task ResendVerification_WithNonexistentEmail_ShowsGenericResponse()
    {
        // Arrange - enable email service
        await Factory.EnableEmailVerificationAsync();

        // Act - request resend for nonexistent email
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();
        await _resendPage.RequestResendAsync("nonexistent@e2e.local");

        // Assert - should show error (security: don't reveal if email exists)
        // The endpoint returns "Unable to send verification email" for security
        var errorMessage = await _resendPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null,
            "Should show error message for security (don't reveal if email exists)");
    }

    [Test]
    public async Task ResendVerification_WithAlreadyVerifiedEmail_ShowsGenericResponse()
    {
        // Arrange - create a verified user
        await Factory.EnableEmailVerificationAsync();

        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("already-verified-resend"))
            .WithStandardPassword()
            .WithEmailVerified(true)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        // Act - request resend for already verified email
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();
        await _resendPage.RequestResendAsync(user.Email);

        // Assert - should show error (don't reveal verification status)
        var errorMessage = await _resendPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Is.Not.Null,
            "Should show error message (can't resend for verified email)");
    }

    [Test]
    public async Task ResendVerification_BackToLoginLink_NavigatesToLogin()
    {
        // Arrange
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();

        // Act - click back to login
        await _resendPage.ClickBackToLoginAsync();

        // Assert - should be on login page
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"));
    }

    [Test]
    public async Task ResendVerification_SendsEmailWithVerificationLink()
    {
        // Arrange - create an unverified user and enable email
        await Factory.EnableEmailVerificationAsync();

        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("check-link"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        // Act - request resend
        await _resendPage.NavigateAsync();
        await _resendPage.WaitForPageAsync();
        await _resendPage.RequestResendAsync(user.Email);
        await _resendPage.WaitForSuccessAsync();

        // Assert - email contains verification token
        var emails = EmailService.GetEmailsTo(user.Email)
            .Where(e => e.Template == EmailTemplate.EmailVerification)
            .ToList();

        Assert.That(emails, Has.Count.EqualTo(1), "Should send exactly one verification email");
        Assert.That(emails[0].Parameters, Does.ContainKey("VerificationToken"),
            "Email should contain verification token");
        Assert.That(emails[0].Parameters?["VerificationToken"], Is.Not.Null.And.Not.Empty,
            "Verification token should not be empty");
    }

    [Test]
    public async Task UnverifiedUser_CannotLogin()
    {
        // Arrange - create an unverified user
        var user = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("cant-login"))
            .WithStandardPassword()
            .WithEmailVerified(false)
            .WithTotpDisabled()
            .AsOwner()
            .BuildAsync();

        // Act - try to login
        await _loginPage.NavigateAsync();
        await _loginPage.LoginAsync(user.Email, user.Password);

        // Assert - should show error about email verification
        Assert.That(await _loginPage.HasErrorMessageAsync(), Is.True,
            "Unverified user should not be able to login");

        var errorMessage = await _loginPage.GetErrorMessageAsync();
        Assert.That(errorMessage, Does.Contain("verify").Or.Contain("email").IgnoreCase,
            "Error should indicate email verification is needed");
    }

    /// <summary>
    /// Creates a verification token in the database for testing.
    /// </summary>
    private async Task<string> CreateVerificationTokenAsync(
        string userId,
        DataModels.TokenType tokenType,
        DateTimeOffset? expiresAt = null)
    {
        using var scope = Factory.Services.CreateScope();
        var tokenRepo = scope.ServiceProvider.GetRequiredService<IVerificationTokenRepository>();

        var token = Guid.NewGuid().ToString("N");
        var tokenDto = new DataModels.VerificationTokenDto
        {
            UserId = userId,
            TokenType = tokenType,
            Token = token,
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(24),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await tokenRepo.CreateAsync(tokenDto);
        return token;
    }
}
