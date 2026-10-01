using System.Text.RegularExpressions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// The navigation links between the anonymous auth pages, on canonical data with email read as
/// configured: the login page only offers "Forgot your password?" / "Resend verification email" when
/// <c>IsPasswordResetEnabledAsync</c> holds, and canonical stores no SendGrid key, so
/// <c>GoldenDataset.Mutate(...).EnableSendGridApiKey</c> adds a dummy one before the app starts.
/// Nothing is written by the tests; no user logs in.
/// </summary>
[TestFixture]
public class LoginLinksGoldenTests : GoldenE2ETestBase
{
    protected override async Task ArrangeDataAsync(AppDbContext context)
    {
        await GoldenDataset.Mutate(context)
            .EnableSendGridApiKey(SharedKeyRing(), "SG.e2e-dummy")
            .ApplyAsync();
    }

    private static Regex UrlEndingIn(string path) => new($@"{Regex.Escape(path)}$");

    [Test]
    public async Task Login_ForgotPasswordLink_OpensTheForgotPasswordPage()
    {
        var login = new LoginPage(Page);
        await login.NavigateAsync();

        await login.ClickForgotPasswordAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/forgot-password"));
        await Expect(new ForgotPasswordPage(Page).PageTitle).ToBeVisibleAsync();
    }

    [Test]
    public async Task Login_ResendVerificationLink_OpensTheResendVerificationPage()
    {
        var login = new LoginPage(Page);
        await login.NavigateAsync();

        await login.ClickResendVerificationAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/resend-verification"));
        await Expect(new ResendVerificationPage(Page).PageTitle).ToBeVisibleAsync();
    }

    [Test]
    public async Task ForgotPassword_SignInLink_ReturnsToLogin()
    {
        var forgot = new ForgotPasswordPage(Page);
        await forgot.NavigateAsync();

        await forgot.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(new LoginPage(Page).EmailField).ToBeVisibleAsync();
    }

    [Test]
    public async Task ResetPassword_SignInLink_ReturnsToLogin()
    {
        // The link renders whatever the token; nothing is looked up until the form is submitted.
        var reset = new ResetPasswordPage(Page);
        await reset.NavigateAsync("not-a-real-token");

        await reset.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(new LoginPage(Page).EmailField).ToBeVisibleAsync();
    }

    [Test]
    public async Task Register_SignInLink_ReturnsToLogin()
    {
        var register = new RegisterPage(Page);
        await register.NavigateAsync();

        await register.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(new LoginPage(Page).EmailField).ToBeVisibleAsync();
    }
}
