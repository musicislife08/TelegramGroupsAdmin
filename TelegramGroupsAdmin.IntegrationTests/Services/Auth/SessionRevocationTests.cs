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
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.Services.Auth;

/// <summary>
/// A live session must stop validating once its user is no longer Active. Anchor: the canonical
/// no-TOTP GlobalAdmin (<see cref="GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminId"/>,
/// machine@canonical.test), which is Active with a verified email.
/// </summary>
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
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

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
    public async Task ValidatorRejectsAfterDisable()
    {
        var sp = await SetUpServicesAsync();
        await using var scope = sp.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var validator = scope.ServiceProvider.GetRequiredService<IUserSessionValidator>();

        const string userId = GoldenDatasetConstants.WebUsers.NoTotpGlobalAdminId;
        var user = await repo.GetByIdAsync(userId);

        // Guard the precondition: the canonical anchor is Active, so its session starts out valid.
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Status, Is.EqualTo(UserStatus.Active), "canonical anchor must be Active");
        var principal = PrincipalFor(userId, user.SecurityStamp);
        Assert.That(await validator.IsStillValidAsync(principal), Is.True, "session of an Active user should be valid");

        await repo.UpdateStatusAsync(userId, UserStatus.Disabled, GoldenDatasetConstants.WebUsers.OwnerId);

        Assert.That(await validator.IsStillValidAsync(principal), Is.False, "session must be rejected once the user is disabled");
    }
}
