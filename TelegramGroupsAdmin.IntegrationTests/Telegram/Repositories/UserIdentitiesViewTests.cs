using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories.Mappings;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Testing.Golden;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for the user_identities view, the enriched views' identity columns, and the
/// repository reads that build identities from them.
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

    [Test]
    public async Task EnrichedReports_ProfileScanAlert_UserCarriesLatestScanFlag()
    {
        // Pending profile-scan alert 188's user is the canonical profile-scan target; read its user id
        // and expected flag from the tables, then compare the enriched view with user_identities.
        await using var ctx = _testHelper!.GetDbContext();
        var row = await ctx.EnrichedReports.SingleAsync(r => r.Id == GoldenDatasetConstants.Reports.PendingProfileScanAlertId);
        Assert.That(row.ProfileUserId, Is.Not.Null);
        var expected = await ctx.UserIdentities.SingleAsync(v => v.TelegramUserId == row.ProfileUserId);

        Assert.That(row.ProfileUserLatestScanExplicit, Is.EqualTo(expected.LatestScanExplicit));
        Assert.That(row.ProfileUserIsBot, Is.EqualTo(expected.IsBot));
    }

    [Test]
    public async Task EnrichedMessages_AuthorFlagsMatchUserIdentities()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var pairs = await (
            from m in ctx.EnrichedMessages
            join v in ctx.UserIdentities on m.UserId equals v.TelegramUserId
            select new { m.LatestScanExplicit, m.IsBot, ViewFlag = v.LatestScanExplicit, ViewIsBot = v.IsBot })
            .ToListAsync();
        Assert.That(pairs, Is.Not.Empty);

        Assert.That(pairs.Where(p => p.LatestScanExplicit != p.ViewFlag || p.IsBot != p.ViewIsBot), Is.Empty);
    }

    [Test]
    public async Task GetUserDetailAsync_ScannedExplicitUser_CarriesExplicitVerdict()
    {
        var explicitId = GoldenDatasetConstants.IdentityService.ScannedTwiceExplicitUserId;
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        var detail = await repository.GetUserDetailAsync(explicitId);

        Assert.That(detail!.User.Verdict, Is.EqualTo(NameVerdict.Explicit));
    }

    [Test]
    public async Task GetChatAdminsAsync_IdentitiesComeFromUserIdentities()
    {
        await using var ctx = _testHelper!.GetDbContext();
        // Prefer a chat whose active admins include a scanned user.
        var chatId = await (
            from ca in ctx.ChatAdmins
            where ca.IsActive
            join v in ctx.UserIdentities on ca.TelegramId equals v.TelegramUserId
            orderby v.LatestScanExplicit == null, ca.ChatId
            select ca.ChatId).FirstAsync();
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IChatAdminsRepository>();

        var admins = await repository.GetChatAdminsAsync(chatId);

        Assert.That(admins, Is.Not.Empty);
        var ids = admins.Select(a => a.User.Id).ToList();
        var expected = (await ctx.UserIdentities.Where(v => ids.Contains(v.TelegramUserId)).ToListAsync())
            .ToDictionary(v => v.TelegramUserId, v => v.ToIdentity());
        foreach (var admin in admins)
            Assert.That(admin.User, Is.EqualTo(expected[admin.User.Id]));
    }

    private ServiceProvider BuildProvider() => new ServiceCollection()
        .AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper!.ConnectionString))
        .AddLogging()
        .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
        .AddScoped<IChatAdminsRepository, ChatAdminsRepository>()
        .BuildServiceProvider();
}
