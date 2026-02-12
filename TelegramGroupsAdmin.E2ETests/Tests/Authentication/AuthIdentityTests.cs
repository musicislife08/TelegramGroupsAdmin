using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// Tests that authenticated user identity is properly threaded through the app.
/// Verifies the full chain: AuthCookieService → cookie → ClaimsIdentity → MainLayout → WebUserIdentity.
/// </summary>
[TestFixture]
public class AuthIdentityTests : SharedAuthenticatedTestBase
{
    [Test]
    public async Task AppBar_ShowsUserEmail_AfterCookieLogin()
    {
        // Arrange - login via cookie injection (same path as production)
        var user = await LoginAsOwnerAsync();

        // Act - navigate to any authenticated page
        await NavigateToAsync("/");
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);

        // Assert - email should be visible in the app bar (rendered by MainLayout)
        var emailElement = Page.GetByText(user.Email);
        await Expect(emailElement).ToBeVisibleAsync();
    }
}
