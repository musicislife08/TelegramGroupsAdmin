using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using TelegramGroupsAdmin.Auth;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;
using TelegramGroupsAdmin.Services.Auth;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Auth;

[TestFixture]
[Category("Integration")]
public class SessionRevocationTests
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
        services.AddScoped<IUserSessionValidator, UserSessionValidator>();
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider;
    }

    private static ClaimsPrincipal PrincipalFor(string userId, string stamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(CustomClaimTypes.SecurityStamp, stamp)
        ], "test"));

    [Test]
    public async Task ValidatorRejectsAfterStampRotation()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<IUserSessionValidator>();

        var userId = await SeedActiveUserAsync(repo);
        var user = await repo.GetByIdAsync(userId);
        Assert.That(user, Is.Not.Null);

        var principal = PrincipalFor(userId, user!.SecurityStamp);
        Assert.That(await validator.IsStillValidAsync(principal), Is.True, "fresh session should be valid");

        await repo.UpdateSecurityStampAsync(userId);
        Assert.That(await validator.IsStillValidAsync(principal), Is.False, "session with old stamp must be rejected");
    }

    [Test]
    public async Task ValidatorRejectsAfterDisable()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<IUserSessionValidator>();

        var userId = await SeedActiveUserAsync(repo);
        var user = await repo.GetByIdAsync(userId);
        var principal = PrincipalFor(userId, user!.SecurityStamp);
        Assert.That(await validator.IsStillValidAsync(principal), Is.True);

        await repo.UpdateStatusAsync(userId, UserStatus.Disabled, "admin");
        Assert.That(await validator.IsStillValidAsync(principal), Is.False);
    }

    /// <summary>
    /// Seeds a minimal Active + EmailVerified user via <see cref="IUserRepository.CreateAsync"/>
    /// and returns its ID. No password hashing is needed for these validator tests —
    /// any non-null hash string satisfies the NOT NULL constraint.
    /// </summary>
    private static async Task<string> SeedActiveUserAsync(IUserRepository repo)
    {
        var userId = Guid.NewGuid().ToString();
        var email = $"session-test-{userId}@integration.test";
        var now = DateTimeOffset.UtcNow;

        var userRecord = new UserRecord(
            WebUser: new WebUserIdentity(userId, email, PermissionLevel.Admin),
            NormalizedEmail: email.ToUpperInvariant(),
            PasswordHash: "not-a-real-hash",
            SecurityStamp: Guid.NewGuid().ToString(),
            InvitedBy: null,
            IsActive: true,
            TotpSecret: null,
            TotpEnabled: false,
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
