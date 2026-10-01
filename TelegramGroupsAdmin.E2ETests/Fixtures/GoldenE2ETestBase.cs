using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.E2ETests;

/// <summary>
/// Base class for E2E tests on canonical golden data. Each test clones a session template,
/// shapes it in <see cref="ArrangeDataAsync"/> (reduce/mutate, precondition read-backs) BEFORE
/// the app starts — so no app cache ever sees the pre-arrangement state — then starts a fresh
/// app instance on the clone. Log in as canonical users; never seed with builders.
/// </summary>
public abstract class GoldenE2ETestBase : E2ETestBase
{
    /// <summary>Template this test's database is cloned from. Override for empty-state tests.</summary>
    protected virtual GoldenTemplate Template => GoldenTemplate.Golden;

    /// <summary>The per-test clone's database name.</summary>
    protected string DatabaseName { get; private set; } = string.Empty;

    /// <summary>
    /// Shape the clone before the app starts: GoldenDataset.Reduce/Mutate and read-back guards
    /// of canonical-edit preconditions. Missing reduce/mutate operations get added to the builders.
    /// </summary>
    protected virtual Task ArrangeDataAsync(AppDbContext context) => Task.CompletedTask;

    /// <summary>A direct context on this test's clone, for arrangement and read-back assertions.</summary>
    protected AppDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(GoldenTemplates.ConnectionStringFor(E2EFixture.BaseConnectionString, DatabaseName))
            .Options);

    protected override async Task<TestWebApplicationFactory> CreateFactoryAsync()
    {
        DatabaseName = E2EFixture.GetUniqueDatabaseName();
        await GoldenTemplates.CloneAsync(E2EFixture.BaseConnectionString, Template, DatabaseName);

        await using (var context = CreateDbContext())
        {
            await ArrangeDataAsync(context);
        }

        return new TestWebApplicationFactory(
            DatabaseName, databaseAlreadyExists: true, sharedKeysDirectory: E2EFixture.SharedKeysDirectory);
    }

    protected override async Task OnFactoryDisposedAsync()
    {
        // The factory drops its database on dispose; this covers a setup that failed before
        // the factory existed (e.g. ArrangeDataAsync threw). DROP … IF EXISTS makes it idempotent.
        if (DatabaseName.Length > 0)
        {
            await GoldenTemplates.DropAsync(E2EFixture.BaseConnectionString, DatabaseName);
        }
    }

    protected Task LoginAsOwnerAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.OwnerId);
    protected Task LoginAsAdminAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.AdminId);
    protected Task LoginAsGlobalAdminAsync() => LoginAsCanonicalAsync(GoldenDatasetConstants.WebUsers.GlobalAdminId);

    /// <summary>
    /// Injects the app's own auth cookie for an existing canonical web user (no SUT writes).
    /// Uses the user's stored security stamp so UserSessionValidator accepts the session.
    /// </summary>
    protected async Task LoginAsCanonicalAsync(string userId)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var user = await users.GetByIdAsync(userId)
            ?? throw new InvalidOperationException($"Canonical user {userId} not found in {DatabaseName}");

        var cookies = scope.ServiceProvider.GetRequiredService<IAuthCookieService>();
        var value = cookies.GenerateCookieValue(user.WebUser, user.SecurityStamp);
        var host = new Uri(Factory.ServerAddress).Host;

        await Context.AddCookiesAsync([
            new Cookie
            {
                Name = cookies.CookieName,
                Value = value,
                Domain = host,
                Path = "/",
                HttpOnly = true,
                Secure = false,
                SameSite = SameSiteAttribute.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
            }
        ]);
    }
}
