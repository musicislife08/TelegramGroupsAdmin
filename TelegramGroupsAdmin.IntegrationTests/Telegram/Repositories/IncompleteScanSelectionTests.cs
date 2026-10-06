using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// The rescan job's selection: untrusted, unbanned, non-bot, not-excluded users whose scan is
/// incomplete (never scanned, or a NameOnly latest row under the retry limit), retried only after
/// RescanAfter. Canonical anchors (<see cref="GoldenDatasetConstants.ProfileRescan"/>), read back first:
/// - NeverScannedUserId: never scanned, exclusion cleared (canonical edit 2026-10-05).
/// - ExcludedNeverScannedUserId: never scanned, excluded (read-only).
/// - NameOnlyLatestUserId: one scan row (528) flag-edited to source NameOnly (canonical edit 2026-10-05).
/// - FullScanLatestUserId: one FullScan row (533), scanned 2026-04-30 (read-only).
/// The batch size is the user count so ordering never hides an anchor.
/// Also pins the job's per-user chat list (GetChatsForUserAsync) on MultiChatUserId and
/// UnmanagedChatOnlyUserId (both read-only).
/// </summary>
[TestFixture]
public class IncompleteScanSelectionTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _provider;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();
        _provider = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString))
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        _testHelper?.Dispose();
    }

    private async Task<List<long>> SelectAsync(DateTimeOffset retryCutoff, int limit)
    {
        await using var ctx = _testHelper!.GetDbContext();
        var everyone = await ctx.TelegramUsers.CountAsync();
        using var scope = _provider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
            .GetEligibleUsersForRescanAsync(everyone, retryCutoff, limit);
    }

    private async Task GuardAsync()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var never = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.NeverScannedUserId);
        var excluded = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.ExcludedNeverScannedUserId);
        var nameOnlyRows = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId).ToListAsync();
        var fullRows = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == GoldenDatasetConstants.ProfileRescan.FullScanLatestUserId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(never.ProfileScannedAt, Is.Null);
            Assert.That(never.ProfileScanExcluded, Is.False, "exclusion cleared by the canonical edit");
            Assert.That(never.IsBanned || never.IsTrusted || never.IsBot, Is.False);
            Assert.That(excluded.ProfileScannedAt, Is.Null);
            Assert.That(excluded.ProfileScanExcluded, Is.True);
            Assert.That(nameOnlyRows, Has.Count.EqualTo(1));
            Assert.That(nameOnlyRows[0].Id, Is.EqualTo(GoldenDatasetConstants.ProfileRescan.NameOnlyLatestScanId));
            Assert.That(nameOnlyRows[0].Source, Is.EqualTo((short)ProfileScanSource.NameOnly));
            Assert.That(fullRows, Has.Count.EqualTo(1));
            Assert.That(fullRows[0].Source, Is.EqualTo((short)ProfileScanSource.FullScan));
        });
    }

    [Test]
    public async Task IncompleteScans_NeverScanned_IsSelectedUnlessExcluded()
    {
        await GuardAsync();

        var selected = await SelectAsync(DateTimeOffset.UtcNow, limit: 3);

        Assert.That(selected, Does.Contain(GoldenDatasetConstants.ProfileRescan.NeverScannedUserId));
        Assert.That(selected, Does.Not.Contain(GoldenDatasetConstants.ProfileRescan.ExcludedNeverScannedUserId));
    }

    [Test]
    public async Task IncompleteScans_NameOnlyLatest_RespectsRetryLimitBoundary()
    {
        await GuardAsync();
        var userId = GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId;

        Assert.That(await SelectAsync(DateTimeOffset.UtcNow, limit: 2), Does.Contain(userId), "1 NameOnly row < limit 2");
        Assert.That(await SelectAsync(DateTimeOffset.UtcNow, limit: 1), Does.Not.Contain(userId), "1 NameOnly row = limit 1");
    }

    [Test]
    public async Task IncompleteScans_NameOnlyLatest_WaitsForRescanAfter()
    {
        await GuardAsync();
        await using var ctx = _testHelper!.GetDbContext();
        var scannedAt = (await ctx.TelegramUsers.AsNoTracking()
            .SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId)).ProfileScannedAt!.Value;

        var selected = await SelectAsync(scannedAt.AddMinutes(-1), limit: 3);

        Assert.That(selected, Does.Not.Contain(GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId));
    }

    [Test]
    public async Task IncompleteScans_FullScanLatest_IsNeverSelected()
    {
        // A complete scan is not rescanned on a timer, however old it is.
        await GuardAsync();

        var selected = await SelectAsync(DateTimeOffset.UtcNow, limit: 3);

        Assert.That(selected, Does.Not.Contain(GoldenDatasetConstants.ProfileRescan.FullScanLatestUserId));
    }

    [Test]
    public async Task ChatsForUser_EveryChatWithMessages_MostRecentlyActiveFirst()
    {
        // The job walks this list to find a chat with profile scanning enabled.
        var userId = GoldenDatasetConstants.ProfileRescan.MultiChatUserId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var chatIds = await ctx.Messages.AsNoTracking()
                .Where(m => m.UserId == userId && m.DeletedAt == null)
                .Select(m => m.ChatId).Distinct().ToListAsync();
            Assert.That(chatIds, Is.EquivalentTo(new[]
            {
                GoldenDatasetConstants.ProfileRescan.MultiChatLatestChatId,
                GoldenDatasetConstants.Chats.MainChatId,
                GoldenDatasetConstants.ProfileRescan.MultiChatOldestChatId
            }));
        }

        using var scope = _provider!.CreateScope();
        var chats = await scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
            .GetChatsForUserAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(chats.Select(c => c.Id), Is.EqualTo(new[]
            {
                GoldenDatasetConstants.ProfileRescan.MultiChatLatestChatId,
                GoldenDatasetConstants.Chats.MainChatId,
                GoldenDatasetConstants.ProfileRescan.MultiChatOldestChatId
            }));
            Assert.That(chats.Select(c => c.ChatName), Has.All.Not.Null, "names come from managed_chats");
        });
    }

    [Test]
    public async Task ChatsForUser_MessagesOnlyOutsideActiveManagedChats_IsEmpty()
    {
        // The chat goes to the scan for its ban celebration and alerts: never one the bot does not manage.
        var userId = GoldenDatasetConstants.ProfileRescan.UnmanagedChatOnlyUserId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var chatIds = await ctx.Messages.AsNoTracking()
                .Where(m => m.UserId == userId && m.DeletedAt == null)
                .Select(m => m.ChatId).Distinct().ToListAsync();
            var activeManaged = await ctx.ManagedChats.AsNoTracking()
                .Where(c => chatIds.Contains(c.ChatId) && c.IsActive && !c.IsDeleted)
                .CountAsync();
            Assert.That(chatIds, Is.Not.Empty, "anchor must have undeleted messages");
            Assert.That(activeManaged, Is.Zero, "anchor's messages must all be outside active managed chats");
        }

        using var scope = _provider!.CreateScope();
        var chats = await scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>()
            .GetChatsForUserAsync(userId);

        Assert.That(chats, Is.Empty);
    }
}
