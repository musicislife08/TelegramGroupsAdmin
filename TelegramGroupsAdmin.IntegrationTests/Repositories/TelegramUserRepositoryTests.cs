using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramGroupsAdmin.Core;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using UiModels = TelegramGroupsAdmin.Telegram.Models;

namespace TelegramGroupsAdmin.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for TelegramUserRepository read paths (user list, tab counts, name search,
/// moderation queue stats) against real PostgreSQL and the canonical dataset.
///
/// Test Infrastructure:
/// - Shared PostgreSQL container (PostgresFixture) — started once per test run
/// - Unique database per test cloned from golden_template — perfect isolation
/// - Canonical dataset available per test — consistent starting state
/// </summary>
[TestFixture]
public class TelegramUserRepositoryTests
{
    private MigrationTestHelper? _testHelper;
    private IServiceProvider? _serviceProvider;
    private ITelegramUserRepository? _repository;

    // Canonical anchor IDs
    // Top MainChat ham author (@unhelpfulgrab, "Squeak Degree", is_banned=false)
    private const long TopHamAuthorId = 9921676191756L;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        var services = new ServiceCollection();

        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(_testHelper.ConnectionString);
        services.AddSingleton(dataSourceBuilder.Build());

        services.AddDbContextFactory<AppDbContext>((_, options) =>
        {
            options.UseNpgsql(_testHelper.ConnectionString);
        });

        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning);
        });

        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();

        _serviceProvider = services.BuildServiceProvider();

        var scope = _serviceProvider.CreateScope();
        _repository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    /// <summary>
    /// The Tagged tab's predicate, mirrored: active, with an admin note, a live tag or a warning in
    /// force — the same active-warning rule WarningCount / HasWarnings apply. Computed in memory from
    /// the clone so the repository's own query is not the oracle.
    /// </summary>
    private static async Task<HashSet<long>> ExpectedTaggedIdsAsync(AppDbContext ctx)
    {
        var now = DateTimeOffset.UtcNow;
        var noted = await ctx.AdminNotes.Select(n => n.TelegramUserId).Distinct().ToHashSetAsync();
        var liveTagged = await ctx.UserTags.Where(t => t.RemovedAt == null).Select(t => t.TelegramUserId).Distinct().ToHashSetAsync();
        var activeMembers = await ctx.TelegramUsers.AsNoTracking()
            .Where(u => u.TelegramUserId != 0 && u.IsActive)
            .Select(u => new { u.TelegramUserId, u.Warnings })
            .ToListAsync();
        return activeMembers
            .Where(u => noted.Contains(u.TelegramUserId) || liveTagged.Contains(u.TelegramUserId)
                        || (u.Warnings?.Any(w => w.ExpiresAt == null || w.ExpiresAt > now) ?? false))
            .Select(u => u.TelegramUserId)
            .ToHashSet();
    }

    private async Task GuardExpiredWarningAnchorAsync(AppDbContext ctx)
    {
        const long id = GoldenDatasetConstants.UsersPage.ExpiredWarningTrustedMemberId;
        var member = await ctx.TelegramUsers.AsNoTracking().Where(u => u.TelegramUserId == id)
            .Select(u => new { u.IsActive, u.Warnings }).SingleAsync();
        Assert.That(member.IsActive, "the anchor must be active, so only its warning decides Tagged");
        Assert.That(member.Warnings, Is.Not.Null.And.Not.Empty);
        Assert.That(member.Warnings!.All(w => w.ExpiresAt < DateTimeOffset.UtcNow), "every warning of the anchor has expired");
        Assert.That(await ctx.AdminNotes.AnyAsync(n => n.TelegramUserId == id), Is.False, "no note");
        Assert.That(await ctx.UserTags.AnyAsync(t => t.TelegramUserId == id), Is.False, "no tag");
    }

    [Test]
    public async Task GetUserTabCountsAsync_TaggedCount_CountsOnlyWarningsInForce()
    {
        await using var ctx = _testHelper!.GetDbContext();
        await GuardExpiredWarningAnchorAsync(ctx);
        var expected = await ExpectedTaggedIdsAsync(ctx);

        var counts = await _repository!.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        Assert.That(counts.TaggedCount, Is.EqualTo(expected.Count),
            "a member whose only warning has expired is not Tagged");
    }

    [Test]
    public async Task GetPagedUsersAsync_Tagged_ExcludesAMemberWhoseOnlyWarningHasExpired()
    {
        await using var ctx = _testHelper!.GetDbContext();
        await GuardExpiredWarningAnchorAsync(ctx);
        var expected = await ExpectedTaggedIdsAsync(ctx);

        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Tagged, skip: 0, take: 1000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        Assert.That(items.Select(i => i.TelegramUserId), Is.EquivalentTo(expected));
        Assert.That(totalCount, Is.EqualTo(expected.Count));
        Assert.That(items.Select(i => i.TelegramUserId), Does.Not.Contain(GoldenDatasetConstants.UsersPage.ExpiredWarningTrustedMemberId));
    }

    [Test]
    public async Task GetPagedUsersAsync_Tagged_IncludesAMemberWhoseWarningIsInForce()
    {
        const long id = GoldenDatasetConstants.UsersPage.WarnedTrustedMemberId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.AdminNotes.AnyAsync(n => n.TelegramUserId == id), Is.False, "no note");
        Assert.That(await ctx.UserTags.AnyAsync(t => t.TelegramUserId == id), Is.False, "no tag");
        await GoldenDataset.Mutate(ctx).ExtendTelegramUserWarnings(id, TimeSpan.FromDays(30)).ApplyAsync();

        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Tagged, skip: 0, take: 1000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var counts = await _repository.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        var warned = items.SingleOrDefault(i => i.TelegramUserId == id);
        Assert.That(warned, Is.Not.Null, "a warning in force makes the member Tagged");
        Assert.That(warned!.WarningCount, Is.EqualTo(1));
        Assert.That(counts.TaggedCount, Is.EqualTo((await ExpectedTaggedIdsAsync(ctx)).Count));
    }

    private static async Task GuardRemovedTagAnchorAsync(AppDbContext ctx)
    {
        const long id = GoldenDatasetConstants.UserTags.RemovedTagUserId;
        var tags = await ctx.UserTags.AsNoTracking().Where(t => t.TelegramUserId == id).ToListAsync();
        Assert.That(tags.Select(t => t.Id), Is.EqualTo(new[] { GoldenDatasetConstants.UserTags.RemovedTagId }), "the anchor's only tag is the edited one");
        Assert.That(tags[0].RemovedAt, Is.Not.Null.And.GreaterThan(tags[0].AddedAt), "the anchor's tag is removed");
        Assert.That(await ctx.AdminNotes.AnyAsync(n => n.TelegramUserId == id), Is.False, "no note");
        Assert.That(await ctx.ChatAdmins.AnyAsync(a => a.TelegramId == id), Is.False, "no admin seat");
        Assert.That(await ctx.TelegramUsers.AsNoTracking().Where(u => u.TelegramUserId == id).Select(u => u.IsActive).SingleAsync(), "active");
    }

    [Test]
    public async Task GetPagedUsersAsync_RemovedTag_DoesNotMarkTheMemberTagged()
    {
        await using var ctx = _testHelper!.GetDbContext();
        await GuardRemovedTagAnchorAsync(ctx);

        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 1000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        var member = items.Single(i => i.TelegramUserId == GoldenDatasetConstants.UserTags.RemovedTagUserId);
        Assert.That(member.IsTagged, Is.False, "a removed tag is not a tag");
    }

    [Test]
    public async Task GetAllWithStatsAsync_RemovedTag_DoesNotMarkTheMemberTagged()
    {
        await using var ctx = _testHelper!.GetDbContext();
        await GuardRemovedTagAnchorAsync(ctx);

        var all = await _repository!.GetAllWithStatsAsync();

        var member = all.Single(i => i.TelegramUserId == GoldenDatasetConstants.UserTags.RemovedTagUserId);
        Assert.That(member.IsTagged, Is.False, "a removed tag is not a tag");
    }

    [Test]
    public async Task GetModerationQueueStatsAsync_TaggedCount_IgnoresRemovedTags()
    {
        await using var ctx = _testHelper!.GetDbContext();
        await GuardRemovedTagAnchorAsync(ctx);
        var noted = await ctx.AdminNotes.Select(n => n.TelegramUserId).ToListAsync();
        var liveTagged = await ctx.UserTags.Where(t => t.RemovedAt == null).Select(t => t.TelegramUserId).ToListAsync();
        var expected = await ctx.TelegramUsers.CountAsync(u => noted.Contains(u.TelegramUserId) || liveTagged.Contains(u.TelegramUserId));

        var stats = await _repository!.GetModerationQueueStatsAsync();

        Assert.That(stats.TaggedCount, Is.EqualTo(expected));
    }

    [TearDown]
    public void TearDown()
    {
        _testHelper?.Dispose();
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Search with Username History Tests

    // Canonical username_history anchors (GoldenDatasetConstants.UsernameHistory). Each test
    // reads the history row and the user's current names back first, so a canonical change
    // fails loudly, and proves the current names do not match the term (the match came via history).

    private async Task GuardPastNameAsync(long userId, string term)
    {
        await using var ctx = _testHelper!.GetDbContext();
        var history = await ctx.UsernameHistory.AsNoTracking().Where(h => h.UserId == userId).ToListAsync();
        Assert.That(history.Any(h => Contains(h.Username, term) || Contains(h.FirstName, term) || Contains(h.LastName, term)),
            Is.True, $"canonical username_history for {userId} must carry '{term}'");
        var user = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
        Assert.That(Contains(user.Username, term) || Contains(user.FirstName, term) || Contains(user.LastName, term),
            Is.False, $"current names of {userId} must not match '{term}'");

        static bool Contains(string? value, string term) =>
            value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastUsername()
    {
        const long userId = GoldenDatasetConstants.UsernameHistory.PastUsernameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastUsername;
        await GuardPastNameAsync(userId, term);

        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 10,
            searchText: term, chatIds: new List<long> { 0L }, // GlobalChatId ⇒ no chat filter
            sortLabel: null, sortDescending: false);

        Assert.That(totalCount, Is.EqualTo(1));
        Assert.That(items[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchMatchesPastFirstName()
    {
        const long userId = GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastFirstName;
        await GuardPastNameAsync(userId, term);

        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 50,
            searchText: term, chatIds: new List<long> { 0L }, // GlobalChatId ⇒ no chat filter
            sortLabel: null, sortDescending: false);

        Assert.That(items.Select(u => u.TelegramUserId), Does.Contain(userId));
    }

    [Test]
    public async Task GetUserTabCountsAsync_IncludesUsersMatchedByPastNames()
    {
        const long userId = GoldenDatasetConstants.UsernameHistory.PastUsernameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastUsername;
        await GuardPastNameAsync(userId, term);

        // The anchor is a banned spammer: only the All and Banned tabs count it.
        var counts = await _repository!.GetUserTabCountsAsync(
            chatIds: new List<long> { 0L }, searchText: term);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(counts.AllCount, Is.EqualTo(1));
            Assert.That(counts.BannedCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task GetUserTabCountsAsync_WithEmptyChatIds_ReturnsZeroCounts()
    {
        // Empty list ⇒ no accessible chats ⇒ nothing visible (0 rows, not global).
        var counts = await _repository!.GetUserTabCountsAsync(
            chatIds: new List<long>(), searchText: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(counts.ActiveCount, Is.EqualTo(0));
            Assert.That(counts.TrustedCount, Is.EqualTo(0));
            Assert.That(counts.BannedCount, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_WithEmptyChatIds_ReturnsNothing()
    {
        // Empty list ⇒ no accessible chats ⇒ 0 items (not global).
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 50,
            searchText: null, chatIds: new List<long>(),
            sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totalCount, Is.EqualTo(0));
            Assert.That(items, Is.Empty);
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_WithGlobalChatId_ReturnsRows()
    {
        // [0] (GlobalChatId) ⇒ global / no filter ⇒ canonical active users are returned.
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 50,
            searchText: null, chatIds: new List<long> { 0L },
            sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totalCount, Is.GreaterThan(0));
            Assert.That(items, Is.Not.Empty);
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_WithSpecificChatId_ReturnsOnlyThatChatsUsers()
    {
        // Scope to MainChat: the prolific canonical ham author (messages in MainChat) is included.
        var (mainChatItems, mainChatTotal) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 1000,
            searchText: null, chatIds: new List<long> { GoldenDatasetConstants.Chats.MainChatId },
            sortLabel: null, sortDescending: false);

        // Scope to a chat with no messages from anyone ⇒ no users.
        const long emptyChatId = -100099999999999L;
        var (emptyChatItems, emptyChatTotal) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 1000,
            searchText: null, chatIds: new List<long> { emptyChatId },
            sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mainChatTotal, Is.GreaterThan(0));
            Assert.That(mainChatItems.Any(u => u.TelegramUserId == TopHamAuthorId), Is.True,
                "Top MainChat ham author should be visible when scoped to MainChat");
            Assert.That(emptyChatTotal, Is.EqualTo(0),
                "A chat with no messages yields no scoped users");
            Assert.That(emptyChatItems, Is.Empty);
        }
    }

    [Test]
    public async Task SearchByNameAsync_MatchesPastUsername()
    {
        const long userId = GoldenDatasetConstants.UsernameHistory.PastUsernameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastUsername;
        await GuardPastNameAsync(userId, term);

        var results = await _repository!.SearchByNameAsync(term, limit: 10);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
    }

    [Test]
    public async Task SearchByNameAsync_MatchesPastFirstName()
    {
        const long userId = GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastFirstName;
        await GuardPastNameAsync(userId, term);

        var results = await _repository!.SearchByNameAsync(term, limit: 10);

        Assert.That(results.Select(u => u.TelegramUserId), Does.Contain(userId));
    }

    [Test]
    public async Task GetPagedUsersAsync_SearchByPastName_DoesNotReturnDifferentUser()
    {
        // Every other history owner exists too; only the one whose history carries the term matches.
        const long userId = GoldenDatasetConstants.UsernameHistory.PastUsernameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastUsername;
        await GuardPastNameAsync(userId, term);
        await using (var ctx = _testHelper!.GetDbContext())
        {
            Assert.That(await ctx.UsernameHistory.Select(h => h.UserId).Distinct().CountAsync(), Is.GreaterThan(1),
                "canonical must have other username_history owners for this to be meaningful");
        }

        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 10,
            searchText: term, chatIds: new List<long> { 0L }, // GlobalChatId ⇒ no chat filter
            sortLabel: null, sortDescending: false);

        Assert.Multiple(() =>
        {
            Assert.That(totalCount, Is.EqualTo(1));
            Assert.That(items[0].TelegramUserId, Is.EqualTo(userId));
        });
    }

    [Test]
    public async Task SearchByNameAsync_DoesNotReturnUserWithoutMatchingHistory()
    {
        // The past-first-name owner has history too, but none that carries the past username.
        const long userId = GoldenDatasetConstants.UsernameHistory.PastUsernameUserId;
        const string term = GoldenDatasetConstants.UsernameHistory.PastUsername;
        await GuardPastNameAsync(userId, term);

        var results = await _repository!.SearchByNameAsync(term, limit: 10);

        Assert.Multiple(() =>
        {
            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].TelegramUserId, Is.EqualTo(userId));
            Assert.That(results.Select(u => u.TelegramUserId),
                Does.Not.Contain(GoldenDatasetConstants.UsernameHistory.PastFirstNameUserId));
        });
    }

    #endregion

    #region All Filter Tests

    private static readonly List<long> GlobalScope = [0L];

    private async Task<AppDbContext> OpenContextAsync()
    {
        var factory = _serviceProvider!.GetRequiredService<IDbContextFactory<AppDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ReturnsKickedJoiner_ThatActiveHides()
    {
        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (activeItems, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allItems.Select(i => i.TelegramUserId), Does.Contain(GoldenDatasetConstants.UsersPage.KickedJoinerId), "All must include a joiner who never passed the gate");
            Assert.That(activeItems.Select(i => i.TelegramUserId), Does.Not.Contain(GoldenDatasetConstants.UsersPage.KickedJoinerId), "Active still excludes unverified users");
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ReturnsUserWithExpiredBanFlagStillSet()
    {
        const long userId = GoldenDatasetConstants.UsersPage.ExpiredBanUserId;

        // Precondition guard: canonical must still carry the edited shape.
        await using (var ctx = await OpenContextAsync())
        {
            var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
            Assert.That(row.IsBanned && row.BanExpiresAt < DateTimeOffset.UtcNow, Is.True,
                "canonical anchor must be is_banned=true with an expired ban_expires_at");
        }

        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (activeItems, _) = await _repository.GetPagedUsersAsync(
            UiModels.UserListFilter.Active, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var (bannedItems, _) = await _repository.GetPagedBannedUsersWithDetailsAsync(
            skip: 0, take: 5000, searchText: null, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(allItems.Select(i => i.TelegramUserId), Does.Contain(userId), "All must include the expired-ban user");
            Assert.That(activeItems.Select(i => i.TelegramUserId), Does.Not.Contain(userId), "Active excludes is_banned rows");
            Assert.That(bannedItems.Select(i => i.TelegramUserId), Does.Not.Contain(userId), "Banned excludes expired bans");
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ExcludesSystemUser()
    {
        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        Assert.That(allItems.Select(i => i.TelegramUserId), Does.Not.Contain(0L));
    }

    [Test]
    public async Task GetPagedUsersAsync_All_ProjectsIsActive()
    {
        var (allItems, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        var kicked = allItems.Single(i => i.TelegramUserId == GoldenDatasetConstants.UsersPage.KickedJoinerId);
        var canonicalActive = allItems.Single(i => i.TelegramUserId == TopHamAuthorId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(kicked.IsActive, Is.False);
            Assert.That(canonicalActive.IsActive, Is.True);
        }
    }

    [Test]
    public async Task GetPagedUsersAsync_All_SearchFindsKickedJoiner()
    {
        var (items, totalCount) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 50,
            searchText: GoldenDatasetConstants.UsersPage.KickedJoinerUsername,
            chatIds: GlobalScope, sortLabel: null, sortDescending: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(totalCount, Is.EqualTo(1));
            Assert.That(items.Single().TelegramUserId, Is.EqualTo(GoldenDatasetConstants.UsersPage.KickedJoinerId));
        }
    }

    [Test]
    public async Task GetUserTabCountsAsync_AllCount_EqualsNonSystemRowCount_UnderGlobalScope()
    {
        var counts = await _repository!.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        int rowCount;
        await using (var ctx = await OpenContextAsync())
        {
            rowCount = await ctx.TelegramUsers.CountAsync(u => u.TelegramUserId != 0);
        }

        Assert.That(counts.AllCount, Is.EqualTo(rowCount));
    }

    [Test]
    public async Task GetPagedUsersAsync_All_RespectsChatScope()
    {
        // The kicked joiner has no messages anywhere, so a MainChat-scoped admin must not see them.
        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.All, skip: 0, take: 5000,
            searchText: null, chatIds: new List<long> { GoldenDatasetConstants.Chats.MainChatId },
            sortLabel: null, sortDescending: false);

        Assert.That(items.Select(i => i.TelegramUserId), Does.Not.Contain(GoldenDatasetConstants.UsersPage.KickedJoinerId));
    }

    [Test]
    public async Task GetPagedUsersAsync_Trusted_IncludesInactiveTrustedUser()
    {
        const long userId = GoldenDatasetConstants.UsersPage.TrustedKickedJoinerId;

        // Precondition guard: canonical must still carry the edited shape.
        await using (var ctx = await OpenContextAsync())
        {
            var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == userId);
            Assert.That(row.IsTrusted && !row.IsActive, Is.True, "canonical anchor must be is_trusted=true, is_active=false");
        }

        var (items, _) = await _repository!.GetPagedUsersAsync(
            UiModels.UserListFilter.Trusted, skip: 0, take: 5000,
            searchText: null, chatIds: GlobalScope, sortLabel: null, sortDescending: false);
        var counts = await _repository.GetUserTabCountsAsync(chatIds: GlobalScope, searchText: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Select(i => i.TelegramUserId), Does.Contain(userId), "Trust is global; gate state must not hide it");
            Assert.That(counts.TrustedCount, Is.EqualTo(items.Count), "count must match the listed rows");
        }
    }

    #endregion
}
