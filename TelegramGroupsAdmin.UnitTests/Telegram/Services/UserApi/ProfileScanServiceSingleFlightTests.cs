using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using TL;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Imaging;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Repositories;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Single-flight sharing and freshness-bypass behaviour of ProfileScanService.
/// The core scan is observed through ITelegramSessionManager: a null client yields an
/// empty result, which is enough to count how many scans actually ran.
/// </summary>
[TestFixture]
public class ProfileScanServiceSingleFlightTests
{
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private ITelegramUserRepository _users = null!;
    private IUserIdentityService _identities = null!;
    private IProfileScoringEngine _scoring = null!;
    private IBotModerationService _moderation = null!;
    private IReportsRepository _reports = null!;
    private IAdminNotificationService _notifications = null!;
    private IUsernameHistoryRepository _history = null!;
    private ServiceProvider _provider = null!;
    private ProfileScanService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = Substitute.For<ITelegramSessionManager>();
        _users = Substitute.For<ITelegramUserRepository>();
        _identities = Substitute.For<IUserIdentityService>();
        _scoring = Substitute.For<IProfileScoringEngine>();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(0m, ProfileScanOutcome.Clean, 0m, 0m, null, null));
        _moderation = Substitute.For<IBotModerationService>();
        _reports = Substitute.For<IReportsRepository>();
        _notifications = Substitute.For<IAdminNotificationService>();
        _history = Substitute.For<IUsernameHistoryRepository>();
        _provider = new ServiceCollection()
            .AddSingleton(_history)
            .AddSingleton(_moderation)
            .AddSingleton(_reports)
            .AddSingleton(_notifications)
            .AddSingleton(_users)
            .AddSingleton(_identities)
            .AddSingleton(_scoring)
            .AddSingleton(Substitute.For<IProfileScanResultsRepository>())
            .AddSingleton(Substitute.For<IConfigService>())
            .BuildServiceProvider();

        _sut = new ProfileScanService(
            _sessions,
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new PipelineMetrics(),
            new RecyclableMemoryStreamManager(),
            Substitute.For<IImageProcessor>(),
            NullLogger<ProfileScanService>.Instance);
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task Scan_IdOnlyIdentity_ResolvesThroughIdentityService()
    {
        _identities.ResolveAsync(7, Arg.Any<CancellationToken>()).Returns(UserIdentity.ForTest(7, "Stored"));
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.FromId(7), null, CancellationToken.None);

        await _identities.Received(1).ResolveAsync(7, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Scan_IdentityWithNames_DoesNotResolve()
    {
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None);

        await _identities.DidNotReceive().ResolveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConcurrentScansForOneUser_ShareOneRun()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");

        var first = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        var second = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        gate.SetResult(null);
        await Task.WhenAll(first, second);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
        Assert.That(await second, Is.SameAs(await first));
    }

    [Test]
    public async Task CancellingFirstCaller_DoesNotCancelSharedScanForSecond()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");
        using var cts = new CancellationTokenSource();

        var first = _sut.ScanUserProfileAsync(user, null, cts.Token);
        var second = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        await cts.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(async () => await first);
        // Third caller arrives after the first was cancelled but before the scan finishes:
        // it must join the still-running scan, not start a duplicate.
        var third = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        gate.SetResult(null);
        var result = await second;

        Assert.That(result.TelegramUserId, Is.EqualTo(7));
        Assert.That(await third, Is.SameAs(result));
        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ForceRequest_DoesNotJoinFreshnessEligibleScan()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser() with { ProfileScannedAt = DateTimeOffset.UtcNow.AddMinutes(-5) });
        var user = UserIdentity.ForTest(7, "A");

        var plain = _sut.ScanUserProfileAsync(user, null, CancellationToken.None);
        var bypass = _sut.ScanUserProfileAsync(user, null, CancellationToken.None, forceRescan: true);
        gate.SetResult(null);
        await Task.WhenAll(plain, bypass);

        await _sessions.Received(2).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConcurrentScansFromDifferentChats_ShareOneScan()
    {
        var gate = new TaskCompletionSource<IWTelegramApiClient?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessions.GetClientForChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        var user = UserIdentity.ForTest(7, "A");

        var a = _sut.ScanUserProfileAsync(user, ChatIdentity.FromId(-100), CancellationToken.None);
        var b = _sut.ScanUserProfileAsync(user, ChatIdentity.FromId(-200), CancellationToken.None);
        gate.SetResult(null);
        await Task.WhenAll(a, b);

        await _sessions.Received(1).GetClientForChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        Assert.That(await b, Is.SameAs(await a));
    }

    [Test]
    public async Task ForceRescan_ScansEvenWhenRecentlyScanned()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser());
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None, forceRescan: true);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithoutBypass_RecentlyScanned_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(RecentlyScannedUser());

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None);

        await _sessions.DidNotReceiveWithAnyArgs().GetAnyClientAsync(default);
    }

    [Test]
    public async Task RecentlyScanned_RenamedSinceTheScan_DoesNotReuseCachedScore()
    {
        var row = RecentlyScannedUser();
        _users.GetByTelegramIdAsync(7, Arg.Any<CancellationToken>()).Returns(row);
        _history.HasChangeSinceAsync(7, row.ProfileScannedAt, Arg.Any<CancellationToken>()).Returns(true);
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(7, "A"), null, CancellationToken.None);

        await _sessions.Received(1).GetAnyClientAsync(Arg.Any<CancellationToken>());
    }

    private static TelegramUser RecentlyScannedUser()
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: 7, Username: null, FirstName: "A", LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: false, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now,
            ProfileScannedAt: now, ProfileScanScore: 1m);
    }

    // ── Live-name observation and rescore rules (client returns a TL.User) ──

    private const long LiveUserId = 7;

    private static TelegramUser StoredRow(string? first, string? last = null) =>
        new(LiveUserId, "stored_handle", first, last, null, null, null,
            IsBot: false, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            ProfileScannedAt: DateTimeOffset.UtcNow.AddMinutes(-5), ProfileScanScore: 10m,
            PersonalChannelId: 0); // an empty TL.UserFull reports personal_channel_id 0, which is what a prior scan stored

    // Resolves through the username strategy (no triggering chat), then returns the full user.
    private IWTelegramApiClient ClientReturning(TL.User? liveUser)
    {
        var resolveUser = new TL.User { id = LiveUserId, access_hash = 1, username = "stored_handle" };
        var client = Substitute.For<IWTelegramApiClient>();
        client.Contacts_ResolveUsername("stored_handle").Returns(new Contacts_ResolvedPeer
        {
            peer = new PeerUser { user_id = LiveUserId },
            users = new Dictionary<long, TL.User> { [LiveUserId] = resolveUser },
            chats = new Dictionary<long, ChatBase>()
        });
        var users = new Dictionary<long, TL.User>();
        if (liveUser is not null) users[LiveUserId] = liveUser;
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).Returns(new Users_UserFull
        {
            full_user = new UserFull(),
            users = users,
            chats = new Dictionary<long, ChatBase>()
        });
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns(client);
        _sessions.GetClientForChatAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(client);
        return client;
    }

    private static TL.User Live(string first, string? last = null) =>
        new() { id = LiveUserId, access_hash = 1, first_name = first, last_name = last, username = "stored_handle" };

    [Test]
    public async Task Scan_WithLiveNames_ObservesOnceAsUserApiScanWithoutRescan()
    {
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Old"));
        ClientReturning(Live("New", "Name"));

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Old"), null, CancellationToken.None);

        await _identities.Received(1).ObserveAsync(
            Arg.Is<ObservedUser>(o => o!.Id == LiveUserId && o.FirstName == "New" && o.LastName == "Name"
                && o.Username == "stored_handle" && o.Source == ObservationSource.UserApiScan),
            Arg.Any<ProfileChangeContext>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Scan_ClientReturnsNoUser_DoesNotObserve()
    {
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Old"));
        ClientReturning(null);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Old"), null, CancellationToken.None);

        await _identities.DidNotReceiveWithAnyArgs().ObserveAsync(default!, default!, default);
    }

    [Test]
    public async Task Scan_RenameDiscoveredByScan_IsFullyRescoredEvenWhenRecentlyScanned()
    {
        // Stored row (the pre-observe snapshot) has the old name; the live name differs.
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Old"));
        ClientReturning(Live("New"));

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Old"), null, CancellationToken.None);

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    [Test]
    public async Task Scan_ProfileUnchanged_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Same"));
        ClientReturning(Live("Same"));

        var result = await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Same"), null, CancellationToken.None);

        await _scoring.DidNotReceiveWithAnyArgs().ScoreAsync(default!, default!, default, default, default, default);
        Assert.That(result.Score, Is.EqualTo(10m));
    }

    [Test]
    public async Task Scan_ProfileUnchanged_RenamedSinceTheScan_IsFullyRescored()
    {
        // The rename was recorded before this scan (by a join observation), so the stored row
        // already holds the live name and the diff sees no change; the history row is the change.
        var row = StoredRow("Same");
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(row);
        _history.HasChangeSinceAsync(LiveUserId, row.ProfileScannedAt, Arg.Any<CancellationToken>()).Returns(true);
        ClientReturning(Live("Same"));

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Same"), null, CancellationToken.None);

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    [Test]
    public async Task Scan_ProfileUnchanged_ForceRescan_IsFullyRescored()
    {
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Same"));
        ClientReturning(Live("Same"));

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Same"), null, CancellationToken.None, forceRescan: true);

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    // ── Actions after scoring use the identity re-resolved after the scan persisted its verdict ──

    private UserIdentity ScanFlagsNameExplicit(ProfileScanOutcome outcome)
    {
        _users.GetByTelegramIdAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(StoredRow("Old"));
        ClientReturning(Live("New"));
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(4.5m, outcome, 2m, 2.5m, "explicit name", ["explicit"],
                ExplicitDisplayText: true));
        var flagged = UserIdentity.ForTest(LiveUserId, "New", verdict: NameVerdict.Explicit);
        _identities.ResolveAsync(LiveUserId, Arg.Any<CancellationToken>()).Returns(flagged);
        return flagged;
    }

    [Test]
    public async Task Scan_BansUser_BanCarriesIdentityResolvedAfterTheScan()
    {
        var flagged = ScanFlagsNameExplicit(ProfileScanOutcome.Banned);
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Old"), ChatIdentity.FromId(-100),
            CancellationToken.None, forceRescan: true);

        Received.InOrder(() =>
        {
            _users.UpdateProfileScanDataAsync(LiveUserId, Arg.Any<string?>(), Arg.Any<long?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<bool>(),
                Arg.Any<bool>(), Arg.Any<decimal>(), Arg.Any<long?>(), Arg.Any<long?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>());
            _identities.ResolveAsync(LiveUserId, Arg.Any<CancellationToken>());
        });
        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.User == flagged), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Scan_HoldsForReview_AlertAndNotificationCarryIdentityResolvedAfterTheScan()
    {
        var flagged = ScanFlagsNameExplicit(ProfileScanOutcome.HeldForReview);
        var chat = ChatIdentity.FromId(-100);

        await _sut.ScanUserProfileAsync(UserIdentity.ForTest(LiveUserId, "Old"), chat,
            CancellationToken.None, forceRescan: true);

        await _reports.Received(1).InsertProfileScanAlertAsync(
            Arg.Is<ProfileScanAlertRecord>(a => a!.User == flagged), Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendProfileScanAlertAsync(
            chat, flagged, Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }
}
