using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Ui.Navigation;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// Security edge case tests for the authentication flow in the WASM UI.
///
/// These tests verify that:
/// 1. Intermediate tokens (issued after password verification) cannot be used to bypass TOTP
/// 2. Protected pages redirect unauthenticated users properly
/// 3. Session state cannot be manipulated to skip security steps
/// Uses WasmSharedE2ETestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmAuthSecurityTests : WasmSharedE2ETestBase
{
    private RegisterPage _registerPage = null!;
    private LoginPage _loginPage = null!;

    [SetUp]
    public void SetUp()
    {
        _registerPage = new RegisterPage(Page);
        _loginPage = new LoginPage(Page);
    }

    /// <summary>
    /// Waits for and asserts the current URL is an auth page (login or register).
    /// Uses Playwright's auto-retry for reliability.
    /// </summary>
    private async Task AssertOnAuthPageAsync(string context, int timeoutMs = 10000)
    {
        // Wait for network to stabilize (SignalR connection for Blazor Server)
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Build regex from constants - ensures tests stay in sync with route definitions
        var loginPattern = Regex.Escape(PageRoutes.Auth.Login);
        var registerPattern = Regex.Escape(PageRoutes.Auth.Register);

        // Use Playwright's auto-retry assertion for URL matching
        await Expect(Page).ToHaveURLAsync(
            new Regex($@".*({loginPattern}|{registerPattern}).*"),
            new() { Timeout = timeoutMs });
    }

    [Test]
    public async Task ProtectedPage_WithoutAuth_RedirectsToLogin()
    {
        // Arrange - no authentication

        // Act - try to access protected home page directly
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.App.Home}");

        // Assert - should redirect to login (or register if first-run)
        await AssertOnAuthPageAsync("Protected pages should redirect unauthenticated users");
    }

    [Test]
    public async Task TotpSetupPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token, try direct navigation

        // Act - try to access TOTP setup page directly without valid intermediate token
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.Auth.SetupTotpPage}");

        // Assert - should redirect to login or register (first-run goes to register)
        await AssertOnAuthPageAsync("Should be redirected to auth page");

        // Verify we're NOT on TOTP setup page (the redirect happened)
        Assert.That(Page.Url, Does.Not.Contain(PageRoutes.Auth.SetupTotpPage),
            "TOTP setup page should redirect without valid intermediate token");
    }

    [Test]
    public async Task TotpSetupPage_WithInvalidUserId_RedirectsToLogin()
    {
        // Arrange - craft URL with invalid userId and token

        // Act - try to access TOTP setup with fake credentials
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.Auth.SetupTotpPage}?userId=fake-user-id&token=fake-token");

        // Assert - wait for redirect away from setup-2fa (API validates token, redirects on failure)
        // Build pattern from constants - match login or register but NOT setup-2fa or verify
        var setupPattern = Regex.Escape(PageRoutes.Auth.SetupTotpPage);
        var verifyPattern = Regex.Escape(PageRoutes.Auth.VerifyTotpPage);
        var loginPattern = Regex.Escape(PageRoutes.Auth.Login);
        var registerPattern = Regex.Escape(PageRoutes.Auth.Register);

        await Expect(Page).ToHaveURLAsync(
            new Regex($@".*({loginPattern}|{registerPattern})(?!.*({setupPattern}|{verifyPattern})).*"),
            new() { Timeout = 10000 });
    }

    [Test]
    public async Task TotpVerifyPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token

        // Act - try to access TOTP verify page directly
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.Auth.VerifyTotpPage}");

        // Assert - should redirect to login or register (not stay on verify page)
        await AssertOnAuthPageAsync("Should be redirected to auth page");

        // Verify we're NOT on TOTP verify page (could be on /login or /register, but not /login/verify)
        var url = Page.Url;
        var isOnVerifyPage = url.EndsWith(PageRoutes.Auth.VerifyTotpPage) ||
                             url.Contains($"{PageRoutes.Auth.VerifyTotpPage}?");
        Assert.That(isOnVerifyPage, Is.False,
            $"TOTP verify page should redirect without valid intermediate token, but URL is: {url}");
    }

    [Test]
    public async Task IntermediateToken_AfterPasswordLogin_CannotAccessProtectedPages()
    {
        // Arrange - create a user and get to the intermediate state
        var email = TestCredentials.GenerateEmail("wasm-security");
        var password = TestCredentials.GeneratePassword();

        // First-run registration
        await _registerPage.NavigateAsync();
        await _registerPage.RegisterAsync(email, password);
        await Page.WaitForURLAsync($"**{PageRoutes.Auth.Login}", new() { Timeout = 10000 });

        // Login with password (gets intermediate token, redirects to TOTP setup)
        await _loginPage.LoginAsync(email, password);
        await Page.WaitForURLAsync($"**{PageRoutes.Auth.SetupTotpPage}**", new() { Timeout = 10000 });

        // Capture current URL to verify we're in TOTP setup
        var totpSetupUrl = Page.Url;
        Assert.That(totpSetupUrl, Does.Contain(PageRoutes.Auth.SetupTotpPage),
            "Should be on TOTP setup page after password login");

        // Act - try to navigate to home page directly (bypassing TOTP)
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.App.Home}");

        // Assert - should redirect back to login (intermediate token doesn't grant access)
        // Build pattern from constants - match login but NOT setup-2fa or verify
        var loginPattern = Regex.Escape(PageRoutes.Auth.Login);
        var setupPattern = Regex.Escape(PageRoutes.Auth.SetupTotpPage);
        var verifyPattern = Regex.Escape(PageRoutes.Auth.VerifyTotpPage);

        await Expect(Page).ToHaveURLAsync(
            new Regex($@".*{loginPattern}(?!.*({setupPattern}|{verifyPattern})).*"),
            new() { Timeout = 10000 });
    }

    [Test]
    public async Task IntermediateToken_AfterPasswordLogin_CannotAccessSettingsPage()
    {
        // Arrange - create a user and get to intermediate state
        var email = TestCredentials.GenerateEmail("wasm-settings-bypass");
        var password = TestCredentials.GeneratePassword();

        // First-run registration and login
        await _registerPage.NavigateAsync();
        await _registerPage.RegisterAsync(email, password);
        await Page.WaitForURLAsync($"**{PageRoutes.Auth.Login}", new() { Timeout = 10000 });

        await _loginPage.LoginAsync(email, password);
        await Page.WaitForURLAsync($"**{PageRoutes.Auth.SetupTotpPage}**", new() { Timeout = 10000 });

        // Act - try to access settings page while in intermediate state
        await Page.GotoAsync($"{BaseUrl}{PageRoutes.Settings.Index}");

        // Assert - should redirect to login (not stay on settings)
        var loginPattern = Regex.Escape(PageRoutes.Auth.Login);
        await Expect(Page).ToHaveURLAsync(
            new Regex($@".*{loginPattern}.*"),
            new() { Timeout = 10000 });

        Assert.That(Page.Url, Does.Not.Contain(PageRoutes.Settings.Index),
            "Settings page should not be accessible with only intermediate token");
    }
}
