using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.Repositories;
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
    private static readonly Regex LoginUrl = new("/(login|register)");

    /// <summary>
    /// Rotating the user's security stamp in the DB (as password/TOTP/permission
    /// changes do) must invalidate an already-issued cookie on the next request.
    /// </summary>
    [Test]
    public async Task SecurityStampRotation_InvalidatesActiveSession()
    {
        // Arrange - authenticated session via cookie carrying the user's current stamp
        var user = await LoginAsAdminAsync();

        await NavigateToAsync("/");
        await Expect(Page).Not.ToHaveURLAsync(LoginUrl);

        // Act - rotate the security stamp out from under the live cookie
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            await users.UpdateSecurityStampAsync(user.Id);
        }

        // Assert - the next full-page load is rejected and redirected to login
        await NavigateToAsync("/");
        await Expect(Page).ToHaveURLAsync(LoginUrl, new() { Timeout = 10000 });
    }

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

        await NavigateToAsync("/");
        await Expect(Page).Not.ToHaveURLAsync(LoginUrl);

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

        // Assert - the existing session no longer validates and is redirected to login
        await NavigateToAsync("/");
        await Expect(Page).ToHaveURLAsync(LoginUrl, new() { Timeout = 10000 });
    }
}
