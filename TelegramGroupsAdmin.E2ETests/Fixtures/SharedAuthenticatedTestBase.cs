using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Helpers;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for E2E tests that require an authenticated user and share a factory instance.
/// Provides convenient login helpers using direct cookie injection for speed.
/// Falls back to UI login when TOTP verification is needed.
///
/// This is the shared-factory version of AuthenticatedTestBase.
/// Use for tests that don't require per-test database isolation.
/// </summary>
/// <remarks>
/// Example usage:
/// <code>
/// public class LoginTests : SharedAuthenticatedTestBase
/// {
///     [Test]
///     public async Task Login_WithValidCredentials_Works()
///     {
///         await LoginAsOwnerAsync();
///         await NavigateToAsync("/");
///         // ... assertions
///     }
/// }
/// </code>
/// </remarks>
public abstract class SharedAuthenticatedTestBase : SharedE2ETestBase
{
    /// <summary>
    /// The currently logged-in test user. Null if not logged in.
    /// </summary>
    protected TestUser? CurrentUser { get; private set; }

    /// <summary>
    /// Creates a test user and logs in as Owner (highest permission level).
    /// Use for tests requiring full administrative access.
    /// </summary>
    protected async Task<TestUser> LoginAsOwnerAsync()
    {
        return await LoginAsAsync(PermissionLevel.Owner);
    }

    /// <summary>
    /// Creates a test user and logs in as GlobalAdmin.
    /// Use for tests requiring global moderation access but not full ownership.
    /// </summary>
    protected async Task<TestUser> LoginAsGlobalAdminAsync()
    {
        return await LoginAsAsync(PermissionLevel.GlobalAdmin);
    }

    /// <summary>
    /// Creates a test user and logs in as Admin (chat-scoped permissions).
    /// Use for tests requiring minimal permissions.
    /// </summary>
    protected async Task<TestUser> LoginAsAdminAsync()
    {
        return await LoginAsAsync(PermissionLevel.Admin);
    }

    /// <summary>
    /// Creates a test user with the specified permission level and logs in.
    /// Uses direct cookie injection for speed (skips UI login flow).
    /// </summary>
    /// <param name="permissionLevel">The permission level for the user</param>
    /// <param name="enableTotp">Whether to enable TOTP (2FA) for the user. Default is false for convenience.</param>
    protected async Task<TestUser> LoginAsAsync(PermissionLevel permissionLevel, bool enableTotp = false)
    {
        var builder = new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail(permissionLevel.ToString().ToLower()))
            .WithStandardPassword()
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
    /// This bypasses the UI login flow for speed. Use LoginViaUiAsync() when
    /// you specifically need to test the login UI itself.
    /// </summary>
    /// <remarks>
    /// Security Note: Cookie injection is safe in test context because:
    /// 1. We use the same IAuthCookieService the production app uses
    /// 2. The cookie is properly signed/encrypted by the app's data protection
    /// 3. This is faster than UI login and doesn't test auth flow (tested separately)
    /// 4. The browser context is isolated per-test, cookies don't leak
    /// </remarks>
    /// <param name="user">The test user to log in as</param>
    /// <param name="handleTotp">Ignored for cookie-based login (TOTP already verified in user setup)</param>
    protected async Task LoginAsAsync(TestUser user, bool handleTotp = true)
    {
        // Use the same IAuthCookieService the app uses to generate cookies
        using var scope = SharedFactory.Services.CreateScope();
        var authCookieService = scope.ServiceProvider.GetRequiredService<IAuthCookieService>();

        // Generate the encrypted cookie value
        var cookieValue = authCookieService.GenerateCookieValue(
            new WebUserIdentity(user.Id, user.Email, user.PermissionLevel));

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
    protected async Task LoginViaUiAsync(TestUser user, bool handleTotp = true)
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
    /// </summary>
    protected async Task LogoutAsync()
    {
        await NavigateToAsync("/logout");
        CurrentUser = null;
    }

    /// <summary>
    /// Creates and returns a test user without logging in.
    /// Useful for tests that need multiple users or permission-based visibility testing.
    /// </summary>
    protected async Task<TestUser> CreateUserAsync(
        PermissionLevel permissionLevel = PermissionLevel.Admin,
        bool emailVerified = true,
        bool totpEnabled = false)
    {
        var builder = new TestUserBuilder(SharedFactory.Services)
            .WithEmail(TestCredentials.GenerateEmail(permissionLevel.ToString().ToLower()))
            .WithStandardPassword()
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
