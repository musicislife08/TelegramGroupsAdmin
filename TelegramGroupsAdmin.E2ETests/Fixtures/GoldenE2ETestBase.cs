using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Npgsql;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.E2ETests.Infrastructure;
using TelegramGroupsAdmin.E2ETests.PageObjects;
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

    /// <summary>
    /// A direct context on this test's clone, for arrangement and read-back assertions.
    /// Pooling is off (GoldenTemplates' policy): the clone is dropped at teardown, so a pooled
    /// connection would only linger as an orphan and trip pg_terminate_backend (57P01).
    /// </summary>
    protected AppDbContext CreateDbContext()
    {
        var cs = new NpgsqlConnectionStringBuilder(
            GoldenTemplates.ConnectionStringFor(E2EFixture.BaseConnectionString, DatabaseName)) { Pooling = false };
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs.ConnectionString).Options);
    }

    /// <summary>
    /// The key ring every golden-test app instance shares (the one the template's encrypted columns were
    /// protected with). For mutate verbs that write encrypted columns in <see cref="ArrangeDataAsync"/>.
    /// </summary>
    protected static IDataProtectionProvider SharedKeyRing() => DataProtectionProvider.Create(
        new DirectoryInfo(E2EFixture.SharedKeysDirectory), b => b.SetApplicationName("TgSpamPreFilter"));

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

    /// <summary>
    /// Logs in through the login page with the shared canonical password and waits for the
    /// post-login redirect. For the no-TOTP canonical users (<c>NoTotpGlobalAdminEmail</c>,
    /// <c>NoTotpAdminEmail</c>); the TOTP-enabled anchors (owner@/admin@) have no TOTP secret and
    /// would land on /login/setup-2fa — use the cookie helpers for them.
    /// </summary>
    protected async Task LoginViaUiAsync(string email)
    {
        var loginPage = new LoginPage(Page);
        await loginPage.NavigateAsync();
        await loginPage.LoginAsync(email, GoldenDatasetConstants.WebUsers.Password);
        await loginPage.WaitForRedirectAsync();
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
