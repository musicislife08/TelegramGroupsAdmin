using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for the user_identities view and TelegramUserRepository.GetIdentitiesAsync.
/// Canonical anchors only (GoldenDatasetConstants.IdentityService); nothing is seeded.
/// </summary>
[TestFixture]
public class UserIdentitiesViewTests
{
    private MigrationTestHelper? _testHelper;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
    }

    [TearDown]
    public void TearDown() => _testHelper?.Dispose();

    [Test]
    public async Task GetIdentitiesAsync_LatestScanWins_UnscannedAndBotsAreUnscanned()
    {
        var explicitId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
        var unscannedId = GoldenDatasetConstants.IdentityService.UnscannedUserId;
        var botId = GoldenDatasetConstants.IdentityService.BotUserId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == explicitId), Is.EqualTo(2));
        Assert.That(await ctx.ProfileScanResults.CountAsync(r => r.UserId == unscannedId), Is.Zero);
        Assert.That((await ctx.TelegramUsers.SingleAsync(u => u.TelegramUserId == botId)).IsBot, Is.True);
        await using var provider = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString))
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        var identities = await repository.GetIdentitiesAsync([explicitId, unscannedId, botId, 1L]);

        Assert.That(identities.Select(i => i.Id), Is.EquivalentTo(new[] { explicitId, unscannedId, botId }));
        Assert.That(identities.Single(i => i.Id == explicitId).Verdict, Is.EqualTo(NameVerdict.Explicit));
        Assert.That(identities.Single(i => i.Id == unscannedId).Verdict, Is.EqualTo(NameVerdict.Unscanned));
        Assert.That(identities.Single(i => i.Id == botId).Verdict, Is.EqualTo(NameVerdict.Unscanned));
    }

    [Test]
    public async Task View_ExposesNullFlagForUnscannedUser()
    {
        await using var ctx = _testHelper!.GetDbContext();

        var row = await ctx.UserIdentities.SingleAsync(v => v.TelegramUserId == GoldenDatasetConstants.IdentityService.UnscannedUserId);

        Assert.That(row.LatestScanExplicit, Is.Null);
    }
}
