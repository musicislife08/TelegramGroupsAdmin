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
        await _helper.CreateDatabaseAndApplyMigrationsAsync();
    }

    [TearDown]
    public void TearDown() => _helper?.Dispose();

    [Test]
    public async Task LoadCanonicalAsync_PopulatesAllThirtyFiveTables()
    {
        await using var ctx = _helper!.GetDbContext();
        await GoldenDataset.LoadCanonicalAsync(ctx, PostgresFixture.SharedDataProtectionProvider);

        Assert.That(await ctx.Users.CountAsync(), Is.GreaterThan(0), "users");
        Assert.That(await ctx.TelegramUsers.CountAsync(), Is.GreaterThan(0), "telegram_users");
        Assert.That(await ctx.ManagedChats.CountAsync(), Is.GreaterThan(0), "managed_chats");
        Assert.That(await ctx.Messages.CountAsync(), Is.EqualTo(407), "messages should be exactly 407");
        Assert.That(await ctx.TrainingLabels.CountAsync(), Is.EqualTo(200), "training_labels should be exactly 200");
        Assert.That(await ctx.WelcomeResponses.CountAsync(), Is.EqualTo(11), "welcome_responses should be exactly 11 (deliberate trim)");
        // 5 tables are intentionally EMPTY in canonical: domain_filters, recovery_codes,
        // image_training_samples, video_training_samples, web_notifications.
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
