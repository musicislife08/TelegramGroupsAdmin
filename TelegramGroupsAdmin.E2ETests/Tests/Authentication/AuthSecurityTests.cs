using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;

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
    /// Helper to wait for URL to match login or register (first-run redirects to register)
    /// </summary>
    private async Task WaitForAuthPageAsync()
    {
        // Wait for page to stabilize after navigation
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Give Blazor a moment to handle client-side routing
        await Task.Delay(500);
    }

    /// <summary>
    /// Asserts the current URL is an auth page (login or register)
    /// </summary>
    private void AssertOnAuthPage(string context)
    {
        var url = Page.Url;
        var isOnAuthPage = url.Contains("/login") || url.Contains("/register");
        Assert.That(isOnAuthPage, Is.True,
            $"{context} - Expected to be on /login or /register but was on: {url}");
    }

    [Test]
    public async Task ProtectedPage_WithoutAuth_RedirectsToLogin()
    {
        // Arrange - no authentication

        // Act - try to access protected home page directly
        await Page.GotoAsync($"{BaseUrl}/");
        await WaitForAuthPageAsync();

        // Assert - should redirect to login (or register if first-run)
        AssertOnAuthPage("Protected pages should redirect unauthenticated users");
    }

    [Test]
    public async Task TotpSetupPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token, try direct navigation

        // Act - try to access TOTP setup page directly without valid intermediate token
        await Page.GotoAsync($"{BaseUrl}/login/setup-2fa");
        await WaitForAuthPageAsync();

        // Assert - should redirect to login or register (first-run goes to register)
        // The key is that we should NOT be on /login/setup-2fa
        Assert.That(Page.Url, Does.Not.Contain("/setup-2fa"),
            "TOTP setup page should redirect without valid intermediate token");
        AssertOnAuthPage("Should be redirected to auth page");
    }

    [Test]
    public async Task TotpSetupPage_WithInvalidUserId_RedirectsToLogin()
    {
        // Arrange - craft URL with invalid userId and token

        // Act - try to access TOTP setup with fake credentials
        await Page.GotoAsync($"{BaseUrl}/login/setup-2fa?userId=fake-user-id&token=fake-token");
        await WaitForAuthPageAsync();

        // Assert - should redirect to login or register (not stay on setup-2fa)
        Assert.That(Page.Url, Does.Not.Contain("/setup-2fa"),
            "TOTP setup page should reject invalid userId and token");
        AssertOnAuthPage("Should be redirected to auth page");
    }

    [Test]
    public async Task TotpVerifyPage_WithoutIntermediateToken_RedirectsToLogin()
    {
        // Arrange - no intermediate token

        // Act - try to access TOTP verify page directly (route is /login/verify)
        await Page.GotoAsync($"{BaseUrl}/login/verify");
        await WaitForAuthPageAsync();

        // Assert - should redirect to login or register (not stay on verify page)
        // Note: URL will contain /login but should NOT be the /login/verify path
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
        await WaitForAuthPageAsync();

        // Assert - should redirect back to login (intermediate token doesn't grant access)
        // User exists so should go to /login not /register
        Assert.That(Page.Url, Does.Contain("/login").And.Not.Contain("/setup-2fa"),
            "Intermediate token should NOT grant access to protected pages - must complete TOTP setup");
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
        await WaitForAuthPageAsync();

        // Assert - should redirect to login (not stay on settings)
        Assert.That(Page.Url, Does.Not.Contain("/settings"),
            "Settings page should not be accessible with only intermediate token");
        Assert.That(Page.Url, Does.Contain("/login"),
            "Should redirect to login page");
    }
}
