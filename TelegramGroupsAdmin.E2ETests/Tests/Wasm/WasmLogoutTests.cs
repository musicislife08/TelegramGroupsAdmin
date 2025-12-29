using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Wasm;

/// <summary>
/// WASM Tests for the Logout page (/logout).
/// Verifies logout functionality clears authentication and redirects to login.
/// Uses WasmSharedAuthenticatedTestBase for faster test execution with shared factory.
/// </summary>
[TestFixture]
public class WasmLogoutTests : WasmSharedAuthenticatedTestBase
{
    [Test]
    public async Task Logout_WhenLoggedIn_RedirectsToLogin()
    {
        // Arrange - login as owner
        await LoginAsOwnerAsync();

        // Act - navigate to logout
        await LogoutAsync();

        // Assert - should redirect to login page
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }

    [Test]
    public async Task Logout_ClearsAuth_CannotAccessProtectedPages()
    {
        // Arrange - login and verify access
        await LoginAsOwnerAsync();
        await NavigateToAsync("/");
        await AssertUrlAsync("/"); // Confirm we're on home

        // Act - logout
        await LogoutAsync();

        // Try to access protected page
        await NavigateToAsync("/");

        // Assert - should be redirected to login (not authorized)
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }
}
