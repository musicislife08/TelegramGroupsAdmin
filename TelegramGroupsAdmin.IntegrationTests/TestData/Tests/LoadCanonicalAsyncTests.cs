using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TelegramGroupsAdmin.IntegrationTests.Fixtures;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;

namespace TelegramGroupsAdmin.IntegrationTests.TestData.Tests;

[TestFixture]
public class LoadCanonicalAsyncTests
{
    private MigrationTestHelper? _helper;

    [SetUp]
    public async Task Setup()
    {
        _helper = new MigrationTestHelper();
        await _helper.CreateDatabaseFromEmptyTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    [Test]
    public async Task LoadCanonicalAsync_PopulatesAllThirtyThreeTables()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);

        Assert.That(await ctx.Users.CountAsync(), Is.GreaterThan(0), "users");
        Assert.That(await ctx.TelegramUsers.CountAsync(), Is.GreaterThan(0), "telegram_users");
        Assert.That(await ctx.ManagedChats.CountAsync(), Is.GreaterThan(0), "managed_chats");
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(409), "messages should be exactly 409");
        Assert.That(await ctx.WelcomeResponses.CountAsync(), Is.EqualTo(11), "welcome_responses should be exactly 11 (deliberate trim)");
        Assert.That(await ctx.BanCelebrationSubscribers.CountAsync(), Is.EqualTo(5),
            "ban_celebration_subscribers should be exactly 5 (approved canonical addition 2026-09-25)");
        Assert.That(await ctx.RecoveryCodes.CountAsync(), Is.EqualTo(GoldenDatasetConstants.WebUsers.StoredTotpGlobalAdminRecoveryCodeCount),
            "recovery_codes should be exactly the StoredTotpGlobalAdmin set (canonical addition 2026-10-02)");
        // 2 tables are intentionally EMPTY in canonical: domain_filters, web_notifications.
    }

    [Test]
    public async Task LoadCanonicalAsync_FillsConfigsEncryptedColumns()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);

        var config = await ctx.Configs.FirstAsync(c => c.ChatId == 0);
        // The post-load step encrypts and writes the api_keys column on the global config.
        Assert.That(config.ApiKeys, Is.Not.Null.And.Not.Empty);
    }
}
