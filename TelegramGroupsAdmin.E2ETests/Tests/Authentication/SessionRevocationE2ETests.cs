using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Constants;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.Services;
using static Microsoft.Playwright.Assertions;

namespace TelegramGroupsAdmin.E2ETests.Tests.Authentication;

/// <summary>
/// End-to-end verification that an active browser session is revoked when its
/// security stamp no longer matches the database. Exercises the real pipeline:
/// encrypted cookie -> cookie middleware -> OnValidatePrincipal -> IUserSessionValidator
/// -> DB -> RejectPrincipal/SignOut -> [Authorize] redirect to /login.
///
/// Full-page navigation (Page.GotoAsync) is used deliberately so the HTTP-edge
/// OnValidatePrincipal handler runs on the next request, rather than waiting on the
/// 2-minute in-circuit revalidation timer.
/// </summary>
[TestFixture]
public class SessionRevocationE2ETests : AuthenticatedTestBase
{
    // The login page itself, with or without a query string; not /login/verify or /register
    private static readonly Regex LoginUrl = new(@"/login(?:\?|$)");

    // The site root, where an authenticated session stays
    private static readonly Regex RootUrl = new(@"^https?://[^/]+/$");

    /// <summary>
    /// Changing a user's permission level rotates the security stamp (forced re-login),
    /// so an existing elevated session is invalidated rather than retaining stale access.
    /// Exercises the real UserManagementService path end to end.
    /// </summary>
    [Test]
    public async Task PermissionChange_InvalidatesActiveSession()
    {
        // Arrange - authenticated Admin session, plus a real Owner to act as the modifier
        // (the audit log has a FK on the actor, so the modifier must be a real user).
        var user = await LoginAsAdminAsync();
        var owner = await new TestUserBuilder(Factory.Services)
            .WithEmail(TestCredentials.GenerateEmail("owner"))
            .WithStandardPassword()
            .WithEmailVerified()
            .AsOwner()
            .BuildAsync();

        // Precondition - the session is live: the root page loads without a redirect
        await NavigateToAsync("/");
        await Expect(Page).ToHaveURLAsync(RootUrl);
        Assert.That(await HasAuthCookieAsync(), Is.True, "the session must start with an auth cookie");

        // Act - the Owner changes this user's permission level (rotates the stamp)
        using (var scope = Factory.Services.CreateScope())
        {
            var userManagement = scope.ServiceProvider.GetRequiredService<IUserManagementService>();
            await userManagement.UpdatePermissionLevelAsync(
                user.Id,
                permissionLevel: (int)PermissionLevel.GlobalAdmin,
                modifiedBy: owner.Id,
                modifierPermissionLevel: (int)PermissionLevel.Owner);
        }

        // Assert - the existing session no longer validates: the request is redirected to login
        // and the rejected cookie is signed out, not just ignored
        await NavigateToAsync("/");
        await Expect(Page).ToHaveURLAsync(LoginUrl, new() { Timeout = 10000 });
        Assert.That(await HasAuthCookieAsync(), Is.False, "the rejected session's auth cookie must be removed");
    }

    private async Task<bool> HasAuthCookieAsync() =>
        (await Context.CookiesAsync()).Any(c => c.Name == AuthenticationConstants.CookieName);
}
