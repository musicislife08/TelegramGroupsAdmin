using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Recovery code storage in the REAL <see cref="UserRepository"/> against a golden-template clone.
/// Anchor: the canonical recovery code set of <see cref="GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminId"/>
/// (perfume@canonical.test), with one plaintext pinned. Each test reads the set back first.
/// </summary>
[TestFixture]
[Category("Integration")]
public class RecoveryCodeRepositoryTests
{
    private const string Holder = GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminId;

    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IUserRepository, UserRepository>();
        _serviceProvider = services.BuildServiceProvider();

        Assert.That(await CountCodesAsync(Holder),
            Is.EqualTo(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCodeCount),
            "canonical anchor must hold its recovery code set");
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider?.Dispose();
        _testHelper?.Dispose();
    }

    private IUserRepository Repository => _serviceProvider!.GetRequiredService<IUserRepository>();

    // The hashing TotpService applies before storing or checking a code
    private static string Hash(string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(code.ToLowerInvariant())));

    private async Task<int> CountCodesAsync(string userId)
    {
        await using var ctx = _testHelper!.GetDbContext();
        return await ctx.RecoveryCodes.CountAsync(rc => rc.UserId == userId);
    }

    [Test]
    public async Task UseRecoveryCodeAsync_StoredCode_IsAcceptedOnlyOnce()
    {
        var codeHash = Hash(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Repository.UseRecoveryCodeAsync(Holder, codeHash), Is.True, "first use");
            Assert.That(await Repository.UseRecoveryCodeAsync(Holder, codeHash), Is.False, "second use");
        }
    }

    [Test]
    public async Task ReplaceRecoveryCodesAsync_EarlierCodesStopWorking_AndOnlyTheNewSetRemains()
    {
        string[] newCodes = ["1111111111111111", "2222222222222222"];

        await Repository.ReplaceRecoveryCodesAsync(Holder, newCodes.Select(Hash).ToList());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CountCodesAsync(Holder), Is.EqualTo(newCodes.Length), "only the new set is stored");
            Assert.That(await Repository.UseRecoveryCodeAsync(Holder, Hash(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCode)),
                Is.False, "a code from the earlier set must stop working");
            Assert.That(await Repository.UseRecoveryCodeAsync(Holder, Hash(newCodes[0])), Is.True, "a code from the new set works");
        }
    }

    [Test]
    public async Task ReplaceRecoveryCodesAsync_ForAnotherUser_LeavesTheHoldersSetAlone()
    {
        const string otherUser = GoldenDatasetConstants.WebUsers.NoTotpAdminId;
        Assert.That(await CountCodesAsync(otherUser), Is.Zero, "the other canonical user starts with no codes");

        await Repository.ReplaceRecoveryCodesAsync(otherUser, [Hash("3333333333333333")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CountCodesAsync(otherUser), Is.EqualTo(1));
            Assert.That(await CountCodesAsync(Holder),
                Is.EqualTo(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCodeCount),
                "replacing one user's set must not touch another user's codes");
            Assert.That(await Repository.UseRecoveryCodeAsync(Holder, Hash(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCode)),
                Is.True, "the holder's stored code still works");
        }
    }
}
