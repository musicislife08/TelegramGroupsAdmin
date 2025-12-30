using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Ui.Navigation;
using TelegramGroupsAdmin.Ui.Server.Services.Auth;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for WASM E2E tests that require an authenticated user and share a factory instance.
/// Provides convenient login helpers using direct cookie injection for speed.
/// Falls back to UI login when TOTP verification is needed.
///
/// This is the WASM equivalent of SharedAuthenticatedTestBase.
/// </summary>
public abstract class WasmSharedAuthenticatedTestBase : WasmSharedE2ETestBase
{
    /// <summary>
    /// The currently logged-in test user. Null if not logged in.
    /// </summary>
    protected WasmTestUser? CurrentUser { get; private set; }

    /// <summary>
    /// Creates a test user and logs in as Owner (highest permission level).
    /// Use for tests requiring full administrative access.
    /// </summary>
    protected async Task<WasmTestUser> LoginAsOwnerAsync()
    {
        return await LoginAsAsync(PermissionLevel.Owner);
    }

    /// <summary>
    /// Creates a test user and logs in as GlobalAdmin.
    /// Use for tests requiring global moderation access but not full ownership.
    /// </summary>
    protected async Task<WasmTestUser> LoginAsGlobalAdminAsync()
    {
        return await LoginAsAsync(PermissionLevel.GlobalAdmin);
    }

    /// <summary>
    /// Creates a test user and logs in as Admin (chat-scoped permissions).
    /// Use for tests requiring minimal permissions.
    /// </summary>
    protected async Task<WasmTestUser> LoginAsAdminAsync()
    {
        return await LoginAsAsync(PermissionLevel.Admin);
    }

    /// <summary>
    /// Creates a test user with the specified permission level and logs in.
    /// Uses direct cookie injection for speed (skips UI login flow).
    /// </summary>
    /// <param name="permissionLevel">The permission level for the user</param>
    /// <param name="enableTotp">Whether to enable TOTP (2FA) for the user. Default is false for convenience.</param>
    protected async Task<WasmTestUser> LoginAsAsync(PermissionLevel permissionLevel, bool enableTotp = false)
    {
        var password = TestCredentials.GeneratePassword();
        var builder = new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail($"wasm-{permissionLevel.ToString().ToLower()}"))
            .WithPassword(password)
            .WithEmailVerified()
            .WithPermissionLevel(permissionLevel);

        if (enableTotp)
        {
            builder.WithTotp(enabled: true);
        }

        var user = await builder.BuildAsync();
        await LoginAsAsync(user, handleTotp: enableTotp);
        return user;
    }

    /// <summary>
    /// Logs in as the specified test user using direct cookie injection.
    /// This bypasses the UI login flow for speed.
    /// </summary>
    protected async Task LoginAsAsync(WasmTestUser user, bool handleTotp = true)
    {
        // Use the same IAuthCookieService the WASM app uses to generate cookies
        using var scope = SharedFactory.Services.CreateScope();
        var authCookieService = scope.ServiceProvider.GetRequiredService<IAuthCookieService>();

        // Generate the encrypted cookie value
        var cookieValue = authCookieService.GenerateCookieValue(
            user.Id,
            user.Email,
            user.PermissionLevel,
            user.SecurityStamp);

        // Extract host and port from the server address
        var baseUri = new Uri(SharedFactory.ServerAddress);

        // Inject the authentication cookie into the browser context
        await Context.AddCookiesAsync([
            new Cookie
            {
                Name = authCookieService.CookieName,
                Value = cookieValue,
                Domain = baseUri.Host,
                Path = "/",
                HttpOnly = true,
                Secure = false, // Tests run over HTTP
                SameSite = SameSiteAttribute.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
            }
        ]);

        CurrentUser = user;
    }

    /// <summary>
    /// Logs in via the UI flow. Use this when testing the login flow itself,
    /// not when you just need an authenticated session.
    /// </summary>
    protected async Task LoginViaUiAsync(WasmTestUser user, bool handleTotp = true)
    {
        var loginPage = new LoginPage(Page);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(user.Email, user.Password);

        // Handle TOTP verification if enabled
        if (handleTotp && user.TotpSecret != null)
        {
            var verifyPage = new LoginVerifyPage(Page);
            await verifyPage.WaitForPageAsync();

            var totpCode = TotpHelper.GenerateCode(user.TotpSecret);
            await verifyPage.VerifyAsync(totpCode);
            await verifyPage.WaitForRedirectAsync();
        }
        else
        {
            await loginPage.WaitForRedirectAsync();
        }

        CurrentUser = user;
    }

    /// <summary>
    /// Logs out the current user by navigating to the logout endpoint.
    /// Waits for the redirect to /login to complete before returning.
    /// </summary>
    protected async Task LogoutAsync()
    {
        await NavigateToAsync(PageRoutes.Auth.Logout);

        // Wait for the logout redirect to /login to complete
        // The Logout page calls the API then navigates with forceLoad: true
        await Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(PageRoutes.Auth.Login)), new() { Timeout = 10000 });

        CurrentUser = null;
    }

    /// <summary>
    /// Creates and returns a test user without logging in.
    /// Useful for tests that need multiple users or permission-based visibility testing.
    /// </summary>
    protected async Task<WasmTestUser> CreateUserAsync(
        PermissionLevel permissionLevel = PermissionLevel.Admin,
        bool emailVerified = true,
        bool totpEnabled = false)
    {
        var password = TestCredentials.GeneratePassword();
        var builder = new WasmTestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail($"wasm-{permissionLevel.ToString().ToLower()}"))
            .WithPassword(password)
            .WithPermissionLevel(permissionLevel);

        if (emailVerified)
        {
            builder.WithEmailVerified();
        }

        if (totpEnabled)
        {
            builder.WithTotp(enabled: true);
        }

        return await builder.BuildAsync();
    }
}
