using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.Data.Extensions;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram.Repositories;

/// <summary>
/// Integration tests for TelegramUserRepository.GetOrUpdateAsync: newest-observation-wins name
/// updates, rename history plus ProfileChange audit in one transaction, and the row lock that makes
/// concurrent writers record exactly one rename. Canonical anchors only
/// (GoldenDatasetConstants.IdentityService); every write in these tests is the assertion subject.
/// </summary>
[TestFixture]
public class TelegramUserRepositoryObserveTests
{
    private MigrationTestHelper? _testHelper;
    private ServiceProvider? _serviceProvider;
    private ITelegramUserRepository? _repository;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _serviceProvider = new ServiceCollection()
            .AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(_testHelper.ConnectionString))
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .BuildServiceProvider();
        _repository = _serviceProvider.GetRequiredService<ITelegramUserRepository>();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
        _testHelper?.Dispose();
    }

    private static ObservedUser Observe(long id, string? first, string? last, string? username, DateTimeOffset at) =>
        new(id, first, last, username, IsBot: false, ObservationSource.BotUpdate, at);

    private static readonly ProfileChangeContext NoContext = new(Chat: null, MessageId: null);

    [Test]
    public async Task Rename_WithProductionDataServices_RecordsTheRename()
    {
        // Production registers the context through AddDataServices, which enables retry-on-failure.
        // The plain UseNpgsql factory in SetUp does not, so this exercises the production context.
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var production = new ServiceCollection()
            .AddDataServices(_testHelper!.ConnectionString)
            .AddLogging()
            .AddScoped<ITelegramUserRepository, TelegramUserRepository>()
            .BuildServiceProvider();
        await using var scope = production.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();
        await using var ctx = _testHelper.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.FirstName, Is.Not.EqualTo("Renamed"));

        var result = await repository.GetOrUpdateAsync(
            Observe(id, "Renamed", before.LastName, before.Username, DateTimeOffset.UtcNow), NoContext);

        Assert.That(result.Renamed, Is.Not.Null);
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
    }

    [Test]
    public async Task Rename_UpdatesNames_WritesOneHistoryAndOneAuditRow()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        var profileChange = (int)UserActionType.ProfileChange;
        var auditBefore = await ctx.UserActions.CountAsync(a => a.UserId == id && a.ActionType == profileChange);
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.FirstName, Is.Not.EqualTo("Renamed"));

        var result = await _repository!.GetOrUpdateAsync(
            Observe(id, "Renamed", before.LastName, before.Username, DateTimeOffset.UtcNow), NoContext);

        Assert.That(result.Renamed, Is.Not.Null);
        Assert.That(result.Renamed!.FirstName, Is.EqualTo(before.FirstName));
        Assert.That(result.User.FirstName, Is.EqualTo("Renamed"));
        var history = await ctx.UsernameHistory.AsNoTracking().Where(h => h.UserId == id).ToListAsync();
        Assert.That(history, Has.Count.EqualTo(1));
        Assert.That(history[0].FirstName, Is.EqualTo(before.FirstName));
        var audit = await ctx.UserActions.AsNoTracking()
            .Where(a => a.UserId == id && a.ActionType == profileChange).ToListAsync();
        Assert.That(audit, Has.Count.EqualTo(auditBefore + 1));
        Assert.That(audit.Single(a => a.Reason != null && a.Reason.Contains("→ Renamed")).SystemIdentifier,
            Is.EqualTo(SystemActorIds.ProfileDiffDetection));
    }

    [Test]
    public async Task SameNames_WritesNothing()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);

        var result = await _repository!.GetOrUpdateAsync(
            Observe(id, before.FirstName, before.LastName, before.Username, DateTimeOffset.UtcNow), NoContext);

        Assert.That(result.Renamed, Is.Null);
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
    }

    [Test]
    public async Task OlderObservation_IsIgnored()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        var t2 = DateTimeOffset.UtcNow;
        var newer = await _repository!.GetOrUpdateAsync(Observe(id, "Newer", null, null, t2), NoContext);
        Assert.That(newer.Renamed, Is.Not.Null);

        var result = await _repository.GetOrUpdateAsync(Observe(id, "Stale", null, null, t2.AddMinutes(-5)), NoContext);

        Assert.That(result.Renamed, Is.Null);
        Assert.That(result.User.FirstName, Is.EqualTo("Newer"));
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
    }

    [Test]
    public async Task RenameThereAndBack_RecordsBothChanges()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
        var original = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(original.FirstName, Is.Not.EqualTo("B"));
        var t = DateTimeOffset.UtcNow;

        var there = await _repository!.GetOrUpdateAsync(
            Observe(id, "B", original.LastName, original.Username, t), NoContext);
        Assert.That(there.Renamed, Is.Not.Null);
        var back = await _repository.GetOrUpdateAsync(
            Observe(id, original.FirstName, original.LastName, original.Username, t.AddSeconds(1)), NoContext);

        Assert.That(back.Renamed, Is.Not.Null);
        Assert.That(back.Renamed!.FirstName, Is.EqualTo("B"));
        Assert.That(back.User.FirstName, Is.EqualTo(original.FirstName));
        Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(2));
    }

    [Test]
    public async Task NonUtcObservedAt_IsStoredAsTheUtcInstant()
    {
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.NamesObservedAt, Is.Null);
        Assert.That(before.FirstName, Is.Not.EqualTo("Offset"));
        var at = new DateTimeOffset(2026, 10, 3, 14, 30, 0, TimeSpan.FromHours(2));

        var result = await _repository!.GetOrUpdateAsync(
            Observe(id, "Offset", before.LastName, before.Username, at), NoContext);

        Assert.That(result.Renamed, Is.Not.Null);
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(after.NamesObservedAt, Is.EqualTo(at));
        Assert.That(after.NamesObservedAt!.Value.UtcDateTime, Is.EqualTo(new DateTime(2026, 10, 3, 12, 30, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task RenameMessageInTheSameSecondAsAScan_RecordsTheRename()
    {
        // A scan is dated by the server clock (milliseconds); Telegram dates a message in whole
        // seconds. A rename sent in the second the scan ran must not lose to the scan's watermark.
        var id = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.NamesObservedAt, Is.Null);
        Assert.That(before.FirstName, Is.Not.EqualTo("Renamed"));
        var second = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var scan = await _repository!.GetOrUpdateAsync(
            new ObservedUser(id, before.FirstName, before.LastName, before.Username, IsBot: false,
                ObservationSource.UserApiScan, second.AddMilliseconds(700)),
            NoContext);
        var afterScan = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        var message = await _repository.GetOrUpdateAsync(
            Observe(id, "Renamed", before.LastName, before.Username, second), NoContext);

        Assert.That(scan.Renamed, Is.Null);
        Assert.That(afterScan.NamesObservedAt, Is.EqualTo(second), "server-clock observations are stored at whole seconds");
        Assert.That(message.Renamed, Is.Not.Null);
        Assert.That(message.User.FirstName, Is.EqualTo("Renamed"));
    }

    [Test]
    public async Task NameUpdate_LeavesPhotoFieldsUntouched()
    {
        var id = GoldenDatasetConstants.IdentityService.PhotoUserId;
        await using var ctx = _testHelper!.GetDbContext();
        var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(before.UserPhotoPath, Is.Not.Null);
        Assert.That(before.PhotoHash, Is.Not.Null);
        Assert.That(before.FirstName, Is.Not.EqualTo("Renamed"));

        var result = await _repository!.GetOrUpdateAsync(
            Observe(id, "Renamed", before.LastName, before.Username, DateTimeOffset.UtcNow), NoContext);

        Assert.That(result.Renamed, Is.Not.Null);
        var after = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
        Assert.That(after.FirstName, Is.EqualTo("Renamed"));
        Assert.That(after.UserPhotoPath, Is.EqualTo(before.UserPhotoPath));
        Assert.That(after.PhotoHash, Is.EqualTo(before.PhotoHash));
        Assert.That(after.PhotoFileUniqueId, Is.EqualTo(before.PhotoFileUniqueId));
    }

    [Test]
    public async Task ConcurrentRenames_ExactlyOneRecordsTheRename()
    {
        var id = GoldenDatasetConstants.IdentityService.RaceUserId;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            Assert.That(await ctx.UsernameHistory.CountAsync(h => h.UserId == id), Is.Zero);
            var before = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
            Assert.That(before.FirstName, Is.Not.EqualTo("Racer"));
        }

        await using var holder = new NpgsqlConnection(_testHelper.ConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT 1 FROM telegram_users WHERE telegram_user_id = @id FOR UPDATE", holder, tx))
        {
            lockCmd.Parameters.AddWithValue("id", id);
            await lockCmd.ExecuteScalarAsync();
        }

        var at = DateTimeOffset.UtcNow;
        var first = _repository!.GetOrUpdateAsync(Observe(id, "Racer", null, null, at), NoContext);
        var second = _repository.GetOrUpdateAsync(Observe(id, "Racer", null, null, at), NoContext);

        ObservedNamesResult[] results;
        try
        {
            await WaitForLockWaitersAsync(expected: 2);
        }
        finally
        {
            // Release the lock and observe both writers even if the probe failed.
            await tx.CommitAsync();
            results = await Task.WhenAll(first, second);
        }

        Assert.That(results.Count(r => r.Renamed is not null), Is.EqualTo(1));
        Assert.That(results.Select(r => r.User.FirstName), Is.All.EqualTo("Racer"));
        await using var verify = _testHelper.GetDbContext();
        Assert.That(await verify.UsernameHistory.CountAsync(h => h.UserId == id), Is.EqualTo(1));
    }

    [Test]
    public async Task FirstObservation_CreatesInactiveUntrustedRow()
    {
        // An id outside the canonical range: the insert is the assertion subject.
        const long newUserId = 999999;
        var at = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var result = await _repository!.GetOrUpdateAsync(Observe(newUserId, "New", "Person", "new_user", at), NoContext);

        Assert.Multiple(() =>
        {
            Assert.That(result.Renamed, Is.Null, "a first observation is not a rename");
            Assert.That(result.User.TelegramUserId, Is.EqualTo(newUserId));
            Assert.That(result.User.Username, Is.EqualTo("new_user"));
            Assert.That(result.User.FirstName, Is.EqualTo("New"));
            Assert.That(result.User.LastName, Is.EqualTo("Person"));
            Assert.That(result.User.IsBanned, Is.False);
            Assert.That(result.User.IsTrusted, Is.False);
            Assert.That(result.User.IsActive, Is.False, "new users start inactive until welcome or a message");
            Assert.That(result.User.BotDmEnabled, Is.False);
        });
    }

    [Test]
    public async Task FirstObservation_OfSystemUser_IsAutoTrusted()
    {
        // 777000 (Telegram service account) is not in canonical, so this creates it.
        const long systemUserId = TelegramGroupsAdmin.Core.TelegramConstants.ServiceAccountUserId;

        var result = await _repository!.GetOrUpdateAsync(
            Observe(systemUserId, "Telegram", null, null, DateTimeOffset.UtcNow), NoContext);

        Assert.That(result.User.IsTrusted, Is.True,
            "system users are auto-trusted via TelegramConstants.IsSystemUser()");
    }

    [Test]
    public async Task MarkActive_OnlyMovesLastSeenForward()
    {
        var id = GoldenDatasetConstants.IdentityService.InactiveUserId;
        DateTimeOffset canonicalLastSeen;
        await using (var ctx = _testHelper!.GetDbContext())
        {
            var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
            Assert.That(row.IsActive, Is.False, "precondition: the anchor starts inactive");
            canonicalLastSeen = row.LastSeenAt;
        }

        // A late-processed older update must not move last_seen_at back, but still marks the user active.
        await _repository!.MarkActiveAsync(id, canonicalLastSeen.AddDays(-1));

        await using (var ctx = _testHelper.GetDbContext())
        {
            var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
            Assert.That(row.LastSeenAt, Is.EqualTo(canonicalLastSeen));
            Assert.That(row.IsActive, Is.True);
        }

        var newer = canonicalLastSeen.AddDays(1);
        await _repository.MarkActiveAsync(id, newer);

        await using (var ctx = _testHelper.GetDbContext())
        {
            var row = await ctx.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == id);
            Assert.That(row.LastSeenAt, Is.EqualTo(newer));
            Assert.That(row.IsActive, Is.True);
        }
    }

    /// <summary>
    /// Polls pg_stat_activity until this test database has the expected number of sessions waiting
    /// on a lock, bounded by elapsed time (fixtures run in parallel, so the writers can be slow to
    /// reach the lock). The probe uses the test's own connection string, so current_database() is
    /// the per-test clone and other tests' sessions are not counted.
    /// </summary>
    private async Task WaitForLockWaitersAsync(int expected)
    {
        await using var probe = new NpgsqlConnection(_testHelper!.ConnectionString);
        await probe.OpenAsync();
        Assert.That(probe.Database, Is.EqualTo(_testHelper.DatabaseName));
        var deadline = Stopwatch.StartNew();
        long waiters = 0;
        while (deadline.Elapsed < LockWaitTimeout)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
                probe);
            waiters = (long)(await cmd.ExecuteScalarAsync())!;
            // Each probe is a database round trip, which paces the loop.
            if (waiters >= expected) return;
        }
        Assert.Fail($"Expected {expected} lock waiters within {LockWaitTimeout}, last saw {waiters}");
    }

    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(30);
}
