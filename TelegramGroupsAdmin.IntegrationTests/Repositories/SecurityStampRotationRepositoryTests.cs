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
/// <see cref="IUserRepository"/> mutations. Each method that changes 2FA state, permission level
/// or account status must rotate the user's <c>SecurityStamp</c> in the same single-row UPDATE,
/// invalidating existing sessions (forced re-login). Exercises the REAL repository against a
/// golden-template clone; every subject is a canonical web user, and each test reads its anchor
/// back first so a later canonical change fails loudly.
/// </summary>
[TestFixture]
[Category("Integration")]
public class SecurityStampRotationRepositoryTests
{
    private const string ModifiedBy = GoldenDatasetConstants.WebUsers.OwnerId;

    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
        _testHelper?.Dispose();
    }

    private async Task<IUserRepository> SetUpRepositoryAsync()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserRepository, UserRepository>();
        _serviceProvider = services.BuildServiceProvider();
        return _serviceProvider.GetRequiredService<IUserRepository>();
    }

    private static async Task<UserRecord> ReadAsync(IUserRepository repo, string userId)
    {
        var user = await repo.GetByIdAsync(userId);
        Assert.That(user, Is.Not.Null, $"canonical web user {userId} must exist");
        return user!;
    }

    [Test]
    public async Task UpdatePermissionLevelAsync_RotatesSecurityStamp()
    {
        var repo = await SetUpRepositoryAsync();
        const string userId = GoldenDatasetConstants.WebUsers.NoTotpAdminId;
        var before = await ReadAsync(repo, userId);
        Assert.That(before.WebUser.PermissionLevel, Is.EqualTo(PermissionLevel.Admin), "canonical anchor must be an Admin");

        await repo.UpdatePermissionLevelAsync(userId, (int)PermissionLevel.GlobalAdmin, ModifiedBy);

        var after = await ReadAsync(repo, userId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.SecurityStamp, Is.Not.EqualTo(before.SecurityStamp), "security stamp must rotate");
            Assert.That(after.WebUser.PermissionLevel, Is.EqualTo(PermissionLevel.GlobalAdmin), "permission level must change");
        }
    }

    [Test]
    public async Task EnableTotpAsync_RotatesSecurityStamp()
    {
        var repo = await SetUpRepositoryAsync();
        const string userId = GoldenDatasetConstants.WebUsers.NoTotpAdminId;
        var before = await ReadAsync(repo, userId);
        Assert.That(before.TotpEnabled, Is.False, "canonical anchor must have TOTP disabled");

        await repo.EnableTotpAsync(userId);

        var after = await ReadAsync(repo, userId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.SecurityStamp, Is.Not.EqualTo(before.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.True, "TOTP must be enabled");
        }
    }

    [Test]
    public async Task DisableTotpAsync_RotatesSecurityStamp()
    {
        var repo = await SetUpRepositoryAsync();
        const string userId = GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminId;
        var before = await ReadAsync(repo, userId);
        Assert.That(before.TotpEnabled, Is.True, "canonical anchor must have TOTP enabled");
        Assert.That(before.TotpSecret, Is.Not.Null, "canonical anchor must carry a stored TOTP secret");

        await repo.DisableTotpAsync(userId);

        var after = await ReadAsync(repo, userId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.SecurityStamp, Is.Not.EqualTo(before.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.False, "TOTP must be disabled");
            Assert.That(after.TotpSecret, Is.EqualTo(before.TotpSecret), "secret must be preserved on disable");
        }
    }

    [Test]
    public async Task ResetTotpAsync_RotatesSecurityStamp()
    {
        var repo = await SetUpRepositoryAsync();
        const string userId = GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminId;
        var before = await ReadAsync(repo, userId);
        Assert.That(before.TotpEnabled, Is.True, "canonical anchor must have TOTP enabled");
        Assert.That(before.TotpSecret, Is.Not.Null, "canonical anchor must carry a stored TOTP secret");

        await repo.ResetTotpAsync(userId);

        var after = await ReadAsync(repo, userId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.SecurityStamp, Is.Not.EqualTo(before.SecurityStamp), "security stamp must rotate");
            Assert.That(after.TotpEnabled, Is.False, "TOTP must be disabled");
            Assert.That(after.TotpSecret, Is.Null, "secret must be cleared on reset");
        }
    }

    // Every status transition the Web Admin Accounts page can make: Disable, Delete, Enable, Restore.
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpAdminId, UserStatus.Active, UserStatus.Disabled, Description = "Disable")]
    [TestCase(GoldenDatasetConstants.WebUsers.NoTotpAdminId, UserStatus.Active, UserStatus.Deleted, Description = "Delete")]
    [TestCase(GoldenDatasetConstants.WebUsers.DisabledAdminId, UserStatus.Disabled, UserStatus.Active, Description = "Enable")]
    [TestCase(GoldenDatasetConstants.WebUsers.DeletedAdminId, UserStatus.Deleted, UserStatus.Active, Description = "Restore")]
    public async Task UpdateStatusAsync_RotatesSecurityStamp(string userId, UserStatus canonicalStatus, UserStatus newStatus)
    {
        var repo = await SetUpRepositoryAsync();
        var before = await ReadAsync(repo, userId);
        Assert.That(before.Status, Is.EqualTo(canonicalStatus), "canonical anchor status");

        await repo.UpdateStatusAsync(userId, newStatus, ModifiedBy);

        var after = await ReadAsync(repo, userId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(after.SecurityStamp, Is.Not.EqualTo(before.SecurityStamp), "security stamp must rotate");
            Assert.That(after.Status, Is.EqualTo(newStatus), "status must change");
            Assert.That(after.IsActive, Is.EqualTo(newStatus == UserStatus.Active), "is_active follows the status");
        }
    }
}
