using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// The rescan job's selection: untrusted, unbanned, non-bot, not-excluded users whose scan is
/// incomplete (never scanned, or a NameOnly latest row under the retry limit), retried only after
/// RescanAfter. Canonical anchors (<see cref="GoldenDatasetConstants.ProfileRescan"/>), read back first:
/// - NeverScannedUserId: never scanned, exclusion cleared (canonical edit 2026-10-05).
/// - ExcludedNeverScannedUserId: never scanned, excluded (read-only).
/// - AttemptedNeverScannedUserId: never scanned, exclusion cleared, an attempt that wrote nothing
///   recorded at AttemptedNeverScannedAt (canonical edit 2026-10-10). Its scan-write tests write it as the assertion subject.
/// - NameOnlyLatestUserId: one scan row (528) flag-edited to source NameOnly (canonical edit 2026-10-05).
/// - FullScanLatestUserId: one FullScan row (533), scanned 2026-04-30 (read-only).
/// The batch size is the user count so ordering never hides an anchor.
/// Also pins the job's per-user chat inputs (GetChatsForUserAsync, HasMessageHistoryAsync) on
/// MultiChatUserId, UnmanagedChatOnlyUserId and UsersPage.KickedJoinerId (all read-only).
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
            .GetUsersWithIncompleteScansAsync(everyone, retryCutoff, limit);
    }

    private async Task GuardAsync()
    {
        await using var ctx = _testHelper!.GetDbContext();
        var never = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.NeverScannedUserId);
        var excluded = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.ExcludedNeverScannedUserId);
        var attempted = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedUserId);
        var nameOnlyRows = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId).ToListAsync();
        var fullRows = await ctx.ProfileScanResults.AsNoTracking()
            .Where(r => r.UserId == GoldenDatasetConstants.ProfileRescan.FullScanLatestUserId).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(never.ProfileScannedAt, Is.Null);
            Assert.That(never.ProfileScanExcluded, Is.False, "exclusion cleared by the canonical edit");
            Assert.That(never.IsBanned || never.IsTrusted || never.IsBot, Is.False);
            Assert.That(never.ProfileScanAttemptedAt, Is.Null, "no attempt recorded");
            Assert.That(attempted.ProfileScannedAt, Is.Null);
            Assert.That(attempted.ProfileScanExcluded, Is.False, "exclusion cleared by the canonical edit");
            Assert.That(attempted.IsBanned || attempted.IsTrusted || attempted.IsBot, Is.False);
            Assert.That(attempted.ProfileScanAttemptedAt, Is.EqualTo(GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedAt));
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
    public async Task IncompleteScans_NeverScannedWithAttempt_WaitsForRescanAfter()
    {
        // An attempt that wrote nothing is retried only once it is older than the cutoff.
        await GuardAsync();
        var userId = GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedUserId;
        var attemptedAt = GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedAt;

        Assert.That(await SelectAsync(attemptedAt.AddMinutes(-1), limit: 3), Does.Not.Contain(userId), "attempt newer than the cutoff");
        Assert.That(await SelectAsync(attemptedAt, limit: 3), Does.Not.Contain(userId), "attempt at the cutoff");
        Assert.That(await SelectAsync(attemptedAt.AddMinutes(1), limit: 3), Does.Contain(userId), "attempt older than the cutoff");
    }

    [Test]
    public async Task IncompleteScans_OrderedByScanOrAttemptTime_NeverTriedFirst()
    {
        // Never scanned and never tried comes first; then by last scan or last attempt, oldest first.
        await GuardAsync();
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var nameOnlyScannedAt = (await ctx.TelegramUsers.AsNoTracking()
                .SingleAsync(u => u.TelegramUserId == GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId)).ProfileScannedAt;
            Assert.That(nameOnlyScannedAt, Is.LessThan(GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedAt),
                "the name-only user was scanned before the attempt");
        }

        var selected = await SelectAsync(DateTimeOffset.UtcNow, limit: 3);

        var never = selected.IndexOf(GoldenDatasetConstants.ProfileRescan.NeverScannedUserId);
        var nameOnly = selected.IndexOf(GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId);
        var attempted = selected.IndexOf(GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedUserId);
        Assert.Multiple(() =>
        {
            Assert.That(new[] { never, nameOnly, attempted }, Has.All.GreaterThanOrEqualTo(0), "all three selected");
            Assert.That(never, Is.LessThan(nameOnly), "never tried before scanned");
            Assert.That(nameOnly, Is.LessThan(attempted), "older scan before newer attempt");
        });
    }

    [Test]
    public async Task RecordScanAttempt_NeverScanned_SetsTheAttemptOnly()
    {
        await GuardAsync();
        var userId = GoldenDatasetConstants.ProfileRescan.NeverScannedUserId;
        var start = DateTimeOffset.UtcNow;

        await Users().RecordScanAttemptAsync(userId);

        await using var ctx = _testHelper!.GetDbContext();
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.Multiple(() =>
        {
            Assert.That(after.ProfileScanAttemptedAt, Is.GreaterThanOrEqualTo(start.AddSeconds(-1)));
            Assert.That(after.ProfileScannedAt, Is.Null, "an attempt is not a scan");
        });
    }

    [Test]
    public async Task RecordScanAttempt_ScannedUser_LeavesItUnset()
    {
        // The column only describes a never-scanned user.
        await GuardAsync();
        var userId = GoldenDatasetConstants.ProfileRescan.NameOnlyLatestUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.That(before.ProfileScanAttemptedAt, Is.Null);

        await Users().RecordScanAttemptAsync(userId);

        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.Multiple(() =>
        {
            Assert.That(after.ProfileScanAttemptedAt, Is.Null);
            Assert.That(after.ProfileScannedAt, Is.EqualTo(before.ProfileScannedAt));
            Assert.That(after.UpdatedAt, Is.EqualTo(before.UpdatedAt));
        });
    }

    [TestCase(ProfileScanSource.NameOnly)]
    [TestCase(ProfileScanSource.FullScan)]
    [TestCase(null)]
    public async Task RecordingAScan_ClearsTheAttempt(ProfileScanSource? source)
    {
        // null = the unchanged-profile path, which only bumps the scan time.
        await GuardAsync();
        var userId = GoldenDatasetConstants.ProfileRescan.AttemptedNeverScannedUserId;
        var users = Users();
        var row = new ProfileScanResultRecord(
            Id: 0, UserId: userId, ScannedAt: PostgresTimestamps.FloorToMicrosecond(DateTimeOffset.UtcNow),
            Score: 0.5m, Outcome: ProfileScanOutcome.Clean, RuleScore: 0.0m, AiScore: 0.5m,
            AiReason: null, AiSignals: null, Source: source ?? ProfileScanSource.FullScan);

        await (source switch
        {
            ProfileScanSource.NameOnly => users.RecordNameOnlyScanAsync(row),
            ProfileScanSource.FullScan => users.RecordFullScanAsync(null, null, null, null, false, null,
                false, false, false, null, null, null, row),
            _ => users.UpdateProfileScannedAtAsync(userId)
        });

        await using var ctx = _testHelper!.GetDbContext();
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.Multiple(() =>
        {
            Assert.That(after.ProfileScannedAt, Is.Not.Null);
            Assert.That(after.ProfileScanAttemptedAt, Is.Null);
        });
    }

    private ITelegramUserRepository Users() =>
        _provider!.CreateScope().ServiceProvider.GetRequiredService<ITelegramUserRepository>();

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
                .Where(m => m.UserId == userId)
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
    public async Task PostedOnlyOutsideActiveManagedChats_NoChatButHasHistory()
    {
        // No active managed chat but message history elsewhere: the job treats the user as no longer
        // a user and skips them entirely (no scan, not even by the global config).
        var userId = GoldenDatasetConstants.ProfileRescan.UnmanagedChatOnlyUserId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var chatIds = await ctx.Messages.AsNoTracking()
                .Where(m => m.UserId == userId)
                .Select(m => m.ChatId).Distinct().ToListAsync();
            var activeManaged = await ctx.ManagedChats.AsNoTracking()
                .Where(c => chatIds.Contains(c.ChatId) && c.IsActive && !c.IsDeleted)
                .CountAsync();
            Assert.That(chatIds, Is.Not.Empty, "anchor must have messages");
            Assert.That(activeManaged, Is.Zero, "anchor's messages must all be outside active managed chats");
        }

        using var scope = _provider!.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var chats = await users.GetChatsForUserAsync(userId);
        var hasHistory = await users.HasMessageHistoryAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(chats, Is.Empty);
            Assert.That(hasHistory, Is.True);
        });
    }

    [Test]
    public async Task NeverPosted_NoChatAndNoHistory()
    {
        // Never posted: the job lets the global config decide.
        var userId = GoldenDatasetConstants.UsersPage.KickedJoinerId;
        await using (var ctx = _testHelper!.GetDbContext())
            Assert.That(await ctx.Messages.CountAsync(m => m.UserId == userId), Is.Zero, "anchor has no messages");

        using var scope = _provider!.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        var chats = await users.GetChatsForUserAsync(userId);
        var hasHistory = await users.HasMessageHistoryAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(chats, Is.Empty);
            Assert.That(hasHistory, Is.False);
        });
    }

    [Test]
    public async Task SoftDeletedMessagesOnly_StillCountAsHistoryInTheirChat()
    {
        // Deleted messages are only marked deleted (the cleanup job is off to keep analytics), so they
        // are still evidence the user posted in that chat.
        var userId = GoldenDatasetConstants.ProfileRescan.SoftDeletedOnlyUserId;
        var chatId = GoldenDatasetConstants.ProfileRescan.SoftDeletedOnlyChatId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var messages = await ctx.Messages.AsNoTracking().Where(m => m.UserId == userId).ToListAsync();
            var chat = await ctx.ManagedChats.AsNoTracking().SingleAsync(c => c.ChatId == chatId);
            Assert.Multiple(() =>
            {
                Assert.That(messages, Is.Not.Empty, "anchor has messages");
                Assert.That(messages.Select(m => m.ChatId), Is.All.EqualTo(chatId), "all in one chat");
                Assert.That(messages.Select(m => m.DeletedAt), Is.All.Not.Null, "all soft-deleted");
                Assert.That(chat.IsActive && !chat.IsDeleted, Is.True, "the chat is an active managed chat");
            });
        }

        using var scope = _provider!.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        var chats = await users.GetChatsForUserAsync(userId);
        var hasHistory = await users.HasMessageHistoryAsync(userId);

        Assert.Multiple(() =>
        {
            Assert.That(chats.Select(c => c.Id), Is.EqualTo(new[] { chatId }));
            Assert.That(hasHistory, Is.True);
        });
    }
}
