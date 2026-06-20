using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Verifies the security-stamp rotation invariant folded into the sensitive
/// <see cref="IUserRepository"/> mutations. Each method that changes 2FA state or
/// permission level must rotate the user's <c>SecurityStamp</c> in the same single-row
/// UPDATE, invalidating existing sessions (forced re-login). Exercises the REAL repository
/// against real Postgres.
/// </summary>
[TestFixture]
[Category("Integration")]
public class SecurityStampRotationRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task<IServiceProvider> SetUpServicesAsync()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromEmptyTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserRepository, UserRepository>();
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    [Test]
    public async Task UpdatePermissionLevelAsync_RotatesSecurityStamp()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var userId = await SeedActiveUserAsync(repo, totpEnabled: false);
        var before = await repo.GetByIdAsync(userId);
        Assert.That(before, Is.Not.Null);

        await repo.UpdatePermissionLevelAsync(userId, (int)PermissionLevel.Owner, "admin");

        var after = await repo.GetByIdAsync(userId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.SecurityStamp, Is.Not.EqualTo(before!.SecurityStamp), "security stamp must rotate");
            Assert.That(after.WebUser.PermissionLevel, Is.EqualTo(PermissionLevel.Owner), "permission level must change");
        });
    }

    [Test]
    public async Task EnableTotpAsync_RotatesSecurityStamp()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var userId = await SeedActiveUserAsync(repo, totpEnabled: false);
        var before = await repo.GetByIdAsync(userId);
        Assert.That(before, Is.Not.Null);

        await repo.EnableTotpAsync(userId);

        var after = await repo.GetByIdAsync(userId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.SecurityStamp, Is.Not.EqualTo(before!.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.True, "TOTP must be enabled");
        });
    }

    [Test]
    public async Task DisableTotpAsync_RotatesSecurityStamp()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var userId = await SeedActiveUserAsync(repo, totpEnabled: true, totpSecret: "seeded-secret");
        var before = await repo.GetByIdAsync(userId);
        Assert.That(before, Is.Not.Null);

        await repo.DisableTotpAsync(userId);

        var after = await repo.GetByIdAsync(userId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.SecurityStamp, Is.Not.EqualTo(before!.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.False, "TOTP must be disabled");
            Assert.That(after.TotpSecret, Is.EqualTo("seeded-secret"), "secret must be preserved on disable");
        });
    }

    [Test]
    public async Task ResetTotpAsync_RotatesSecurityStamp()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var userId = await SeedActiveUserAsync(repo, totpEnabled: true, totpSecret: "seeded-secret");
        var before = await repo.GetByIdAsync(userId);
        Assert.That(before, Is.Not.Null);

        await repo.ResetTotpAsync(userId);

        var after = await repo.GetByIdAsync(userId);
        Assert.That(after, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(after!.SecurityStamp, Is.Not.EqualTo(before!.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.False, "TOTP must be disabled");
            Assert.That(after.TotpSecret, Is.Null, "secret must be cleared on reset");
        });
    }

    /// <summary>
    /// Seeds a minimal Active + EmailVerified user via <see cref="IUserRepository.CreateAsync"/>
    /// and returns its ID. TOTP fields are configurable per test. No password hashing is needed —
    /// any non-null hash string satisfies the NOT NULL constraint.
    /// </summary>
    private static async Task<string> SeedActiveUserAsync(
        IUserRepository repo,
        bool totpEnabled,
        string? totpSecret = null)
    {
        var userId = Guid.NewGuid().ToString();
        var email = $"stamp-test-{userId}@integration.test";
        var now = DateTimeOffset.UtcNow;

        var userRecord = new UserRecord(
            WebUser: new WebUserIdentity(userId, email, PermissionLevel.Admin),
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: "not-a-real-hash",
            SecurityStamp: Guid.NewGuid().ToString(),
            InvitedBy: null,
            IsActive: true,
            TotpSecret: totpSecret,
            TotpEnabled: totpEnabled,
            TotpSetupStartedAt: null,
            CreatedAt: now,
            LastLoginAt: null,
            Status: UserStatus.Active,
            ModifiedBy: null,
            ModifiedAt: null,
            EmailVerified: true,
            EmailVerificationToken: null,
            EmailVerificationTokenExpiresAt: null,
            PasswordResetToken: null,
            PasswordResetTokenExpiresAt: null,
            FailedLoginAttempts: 0,
            LockedUntil: null
        );

        await repo.CreateAsync(userRecord);
        return userId;
    }
}
