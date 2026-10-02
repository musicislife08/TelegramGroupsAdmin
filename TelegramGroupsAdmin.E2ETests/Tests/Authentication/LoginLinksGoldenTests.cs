using System.Text.RegularExpressions;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Testing.Golden;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// The navigation links between the anonymous auth pages, on canonical data with email read as
/// configured (the walk from the login page through forgot password, register and reset password and back,
/// plus the resend-verification link): the login page only offers "Forgot your password?" / "Resend verification email" when
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
    public async Task Login_ForgotPasswordLink_OpensTheForgotPasswordPage_AndEachAuthPageLinksBackToLogin()
    {
        var login = new LoginPage(Page);
        await login.NavigateAsync();

        // Login -> forgot password, and back through its "sign in" link.
        await login.ClickForgotPasswordAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/forgot-password"));
        var forgot = new ForgotPasswordPage(Page);
        await Expect(forgot.PageTitle).ToBeVisibleAsync();

        await forgot.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(login.EmailField).ToBeVisibleAsync();

        // Register and back.
        var register = new RegisterPage(Page);
        await register.NavigateAsync();

        await register.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(login.EmailField).ToBeVisibleAsync();

        // Reset password and back: the link renders whatever the token; nothing is looked up until the form is submitted.
        var reset = new ResetPasswordPage(Page);
        await reset.NavigateAsync("not-a-real-token");

        await reset.ClickSignInLinkAsync();

        await Expect(Page).ToHaveURLAsync(UrlEndingIn("/login"));
        await Expect(login.EmailField).ToBeVisibleAsync();
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
}
