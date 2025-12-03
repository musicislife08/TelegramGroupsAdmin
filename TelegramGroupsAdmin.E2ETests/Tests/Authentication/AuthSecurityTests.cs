using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Security edge case tests for the authentication flow.
///
/// These tests verify that:
/// 1. Intermediate tokens (issued after password verification) cannot be used to bypass TOTP
/// 2. Protected pages redirect unauthenticated users properly
/// 3. Session state cannot be manipulated to skip security steps
/// </summary>
[TestFixture]
public class AuthSecurityTests : E2ETestBase
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

        // Use Playwright's auto-retry assertion for URL matching
        await Expect(Page).ToHaveURLAsync(
            new Regex(@".*/(?:login|register).*"),
            new() { Timeout = timeoutMs });
    }

    [Test]
    public async Task ProtectedPage_WithoutAuth_RedirectsToLogin()
    {
        // Arrange - no authentication

        // Act - try to access protected home page directly
        await Page.GotoAsync($"{BaseUrl}/");

        // Assert - should redirect to login (or register if first-run)
        await AssertOnAuthPageAsync("Protected pages should redirect unauthenticated users");
    }

    [Test]
    public async Task TotpSetupPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token, try direct navigation

        // Act - try to access TOTP setup page directly without valid intermediate token
        await Page.GotoAsync($"{BaseUrl}/login/setup-2fa");

        // Assert - should redirect to login or register (first-run goes to register)
        await AssertOnAuthPageAsync("Should be redirected to auth page");

        // Verify we're NOT on /login/setup-2fa (the redirect happened)
        Assert.That(Page.Url, Does.Not.Contain("/setup-2fa"),
            "TOTP setup page should redirect without valid intermediate token");
    }

    [Test]
    public async Task TotpSetupPage_WithInvalidUserId_RedirectsToLogin()
    {
        // Arrange - craft URL with invalid userId and token

        // Act - try to access TOTP setup with fake credentials
        await Page.GotoAsync($"{BaseUrl}/login/setup-2fa?userId=fake-user-id&token=fake-token");

        // Assert - should redirect to login or register (not stay on setup-2fa)
        await AssertOnAuthPageAsync("Should be redirected to auth page");

        // Verify we're NOT on setup-2fa (the redirect happened)
        Assert.That(Page.Url, Does.Not.Contain("/setup-2fa"),
            "TOTP setup page should reject invalid userId and token");
    }

    [Test]
    public async Task TotpVerifyPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token

        // Act - try to access TOTP verify page directly (route is /login/verify)
        await Page.GotoAsync($"{BaseUrl}/login/verify");

        // Assert - should redirect to login or register (not stay on verify page)
        await AssertOnAuthPageAsync("Should be redirected to auth page");

        // Verify we're NOT on /login/verify (could be on /login or /register, but not /login/verify)
        var url = Page.Url;
        var isOnVerifyPage = url.EndsWith("/login/verify") || url.Contains("/login/verify?");
        Assert.That(isOnVerifyPage, Is.False,
            $"TOTP verify page should redirect without valid intermediate token, but URL is: {url}");
    }

    [Test]
    public async Task IntermediateToken_AfterPasswordLogin_CannotAccessProtectedPages()
    {
        // Arrange - create a user and get to the intermediate state
        var email = TestCredentials.GenerateEmail("security");
        var password = TestCredentials.GeneratePassword();

        // First-run registration
        await _registerPage.NavigateAsync();
        await _registerPage.RegisterAsync(email, password);
        await Page.WaitForURLAsync("**/login", new() { Timeout = 10000 });

        // Login with password (gets intermediate token, redirects to TOTP setup)
        await _loginPage.LoginAsync(email, password);
        await Page.WaitForURLAsync("**/login/setup-2fa**", new() { Timeout = 10000 });

        // Capture current URL to verify we're in TOTP setup
        var totpSetupUrl = Page.Url;
        Assert.That(totpSetupUrl, Does.Contain("/login/setup-2fa"),
            "Should be on TOTP setup page after password login");

        // Act - try to navigate to home page directly (bypassing TOTP)
        await Page.GotoAsync($"{BaseUrl}/");

        // Assert - should redirect back to login (intermediate token doesn't grant access)
        // User exists so should go to /login not /register
        await Expect(Page).ToHaveURLAsync(
            new Regex(@".*/login(?!\/(setup-2fa|verify)).*"),
            new() { Timeout = 10000 });
    }

    [Test]
    public async Task IntermediateToken_AfterPasswordLogin_CannotAccessSettingsPage()
    {
        // Arrange - create a user and get to intermediate state
        var email = TestCredentials.GenerateEmail("settings-bypass");
        var password = TestCredentials.GeneratePassword();

        // First-run registration and login
        await _registerPage.NavigateAsync();
        await _registerPage.RegisterAsync(email, password);
        await Page.WaitForURLAsync("**/login", new() { Timeout = 10000 });

        await _loginPage.LoginAsync(email, password);
        await Page.WaitForURLAsync("**/login/setup-2fa**", new() { Timeout = 10000 });

        // Act - try to access settings page while in intermediate state
        await Page.GotoAsync($"{BaseUrl}/settings");

        // Assert - should redirect to login (not stay on settings)
        await Expect(Page).ToHaveURLAsync(
            new Regex(@".*/login.*"),
            new() { Timeout = 10000 });

        Assert.That(Page.Url, Does.Not.Contain("/settings"),
            "Settings page should not be accessible with only intermediate token");
    }
}
