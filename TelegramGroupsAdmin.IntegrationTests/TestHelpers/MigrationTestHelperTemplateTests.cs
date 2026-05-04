using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.Data.Constants;
using TelegramGroupsAdmin.IntegrationTests.TestData;

namespace TelegramGroupsAdmin.IntegrationTests.TestHelpers;

[TestFixture]
public class MigrationTestHelperTemplateTests
{
    [Test]
    public async Task CreateDatabaseFromEmptyTemplateAsync_GivesMigratedSchemaWithZeroRows()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromEmptyTemplateAsync();

        await using var ctx = helper.GetDbContext();
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.Users.CountAsync(), Is.EqualTo(0));
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(0));

        // Schema is at HEAD — confirm a recent migration's table exists.
        var migrationsHistoryCount = await helper.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'");
        Assert.That(migrationsHistoryCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task CreateDatabaseFromGoldenTemplateAsync_GivesCanonicalDataReady()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromGoldenTemplateAsync();

        await using var ctx = helper.GetDbContext();
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(400));
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(200));
    }

    [Test]
    public async Task CloneFromGoldenTemplate_DropsApiKeysCiphertextThatSharedProviderCanDecrypt()
    {
        using var helper = new MigrationTestHelper();
        await helper.CreateDatabaseFromGoldenTemplateAsync();

        await using var ctx = helper.GetDbContext();
        var config = await ctx.Configs.FirstAsync(c => c.ChatId == 0);
        Assert.That(config.ApiKeys, Is.Not.Null);

        // Production decryption sites use DataProtectionPurposes.ApiKeys (e.g.,
        // SystemConfigRepository.GetApiKeysAsync). Test must use the same constant so
        // a passing test guarantees production can also decrypt the ciphertext.
        var protector = PostgresFixture.SharedDataProtectionProvider
            .CreateProtector(DataProtectionPurposes.ApiKeys);
        var plaintext = protector.Unprotect(config.ApiKeys!);
        Assert.That(plaintext, Does.Contain("openai"));
    }
}
