using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TL;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
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
/// The name-only fallback of ProfileScanService: when a scan cannot read the profile (no session,
/// unresolvable, full profile not fetched, timeout, FLOOD_WAIT) the name is scored alone, stored as a
/// NameOnly row and acted on like a scan, on every scan path. Scans leave the exclude flag alone.
/// The real service runs; Telegram, the scorer and the repositories are faked.
/// </summary>
[TestFixture]
public class ProfileScanServiceNameOnlyTests
{
    private const long UserId = 7;
    private const long ChatId = -100;
    private static readonly ChatIdentity Chat = ChatIdentity.FromId(ChatId);
    private static readonly UserIdentity Named = UserIdentity.ForTest(UserId, "Sam", "Rivera", "sam_rivera");

#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private ITelegramUserRepository _users = null!;
    private IUserIdentityService _identities = null!;
    private IProfileScoringEngine _scoring = null!;
    private IProfileScanResultsRepository _results = null!;
    private IConfigService _config = null!;
    private IBotModerationService _moderation = null!;
    private IReportsRepository _reports = null!;
    private IAdminNotificationService _notifications = null!;
    private CapturingLogger<ProfileScanService> _logs = null!;
    private ServiceProvider _provider = null!;
    private ProfileScanService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sessions = Substitute.For<ITelegramSessionManager>();
        _users = Substitute.For<ITelegramUserRepository>();
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow());
        _identities = Substitute.For<IUserIdentityService>();
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(Named);
        _scoring = Substitute.For<IProfileScoringEngine>();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(0m, ProfileScanOutcome.Clean, 0m, 0m, null, null));
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(1.0m, ProfileScanOutcome.Clean));
        _results = Substitute.For<IProfileScanResultsRepository>();
        _config = Substitute.For<IConfigService>();
        _moderation = Substitute.For<IBotModerationService>();
        _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>())
            .Returns(new ModerationResult { Success = true });
        _reports = Substitute.For<IReportsRepository>();
        _notifications = Substitute.For<IAdminNotificationService>();
        _logs = new CapturingLogger<ProfileScanService>();
        _provider = new ServiceCollection()
            .AddSingleton(Substitute.For<IUsernameHistoryRepository>())
            .AddSingleton(_moderation)
            .AddSingleton(_reports)
            .AddSingleton(_notifications)
            .AddSingleton(_users)
            .AddSingleton(_identities)
            .AddSingleton(_scoring)
            .AddSingleton(_results)
            .AddSingleton(_config)
            .BuildServiceProvider();
        _sut = NewSut(TimeSpan.FromSeconds(45));
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    private ProfileScanService NewSut(TimeSpan timeout) => new(
        _sessions,
        _provider.GetRequiredService<IServiceScopeFactory>(),
        new PipelineMetrics(),
        new RecyclableMemoryStreamManager(),
        Substitute.For<IImageProcessor>(),
        _logs)
    { ScanTimeout = timeout };

    // How the gate (join, first message, rename) and the rescan job call the service: with a chat.
    private Task<ProfileScanResult> GateScanAsync(UserIdentity? user = null) =>
        _sut.ScanUserProfileAsync(user ?? Named, Chat, CancellationToken.None);

    private static ScoringResult NameOnlyScore(decimal score, ProfileScanOutcome outcome) =>
        new(score, outcome, 0m, score, "name judged", ["name_signal"], PromotionalDisplayText: true);

    private static TelegramUser StoredRow(
        bool isBot = false, DateTimeOffset? scannedAt = null, decimal? score = null, long? personalChannelId = null,
        bool excluded = false) =>
        new(UserId, "sam_rivera", "Sam", "Rivera", null, null, null,
            IsBot: isBot, IsTrusted: false, IsBanned: false, KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: DateTimeOffset.UtcNow, LastSeenAt: DateTimeOffset.UtcNow,
            CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            ProfileScannedAt: scannedAt, ProfileScanScore: score, PersonalChannelId: personalChannelId,
            ProfileScanExcluded: excluded);

    private void NoSession() =>
        _sessions.GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

    private IWTelegramApiClient Client()
    {
        var client = Substitute.For<IWTelegramApiClient>();
        _sessions.GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>()).Returns(client);
        return client;
    }

    // Resolves through the username strategy, then returns a full user with an empty profile.
    private IWTelegramApiClient ResolvingClient()
    {
        var live = new TL.User { id = UserId, access_hash = 1, first_name = "Sam", last_name = "Rivera", username = "sam_rivera" };
        var client = Client();
        client.Contacts_ResolveUsername("sam_rivera").Returns(new Contacts_ResolvedPeer
        {
            peer = new PeerUser { user_id = UserId },
            users = new Dictionary<long, TL.User> { [UserId] = live },
            chats = new Dictionary<long, ChatBase>()
        });
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).Returns(new Users_UserFull
        {
            full_user = new UserFull(),
            users = new Dictionary<long, TL.User> { [UserId] = live },
            chats = new Dictionary<long, ChatBase>()
        });
        return client;
    }

    private async Task AssertNameOnlyScanRanAsync(ProfileScanResult result)
    {
        await _scoring.Received(1).ScoreNameOnlyAsync(
            Arg.Is<UserIdentity>(u => u!.Id == UserId && u.FirstName == "Sam" && u.Username == "sam_rivera"),
            4.5m, 2.0m, Arg.Any<CancellationToken>());
        await _results.Received(1).InsertAsync(
            Arg.Is<ProfileScanResultRecord>(r => r!.UserId == UserId && r.Source == ProfileScanSource.NameOnly
                && r.Score == 1.0m && r.Outcome == ProfileScanOutcome.Clean && r.RuleScore == 0m && r.AiScore == 1.0m
                && r.PromotionalDisplayText && !r.ExplicitDisplayText && r.AiSignals == "name_signal"),
            Arg.Any<CancellationToken>());
        await _users.Received(1).UpdateProfileScanScoreAsync(UserId, 1.0m, Arg.Any<CancellationToken>());
        Assert.Multiple(() =>
        {
            Assert.That(result.Source, Is.EqualTo(ProfileScanSource.NameOnly));
            Assert.That(result.SkipReason, Is.Null);
            Assert.That(result.Score, Is.EqualTo(1.0m));
            Assert.That(result.Outcome, Is.EqualTo(ProfileScanOutcome.Clean));
            Assert.That(result.PromotionalDisplayText, Is.True);
        });
    }

    // ── Triggers ──

    [Test]
    public async Task NoSession_RunsNameOnlyScan()
    {
        NoSession();

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
        await _users.DidNotReceiveWithAnyArgs().UpdateProfileScanDataAsync(
            default, default, default, default, default, default, default, default, default, default, default, default, default, default, default);
    }

    [Test]
    public async Task UserNotResolvable_RunsNameOnlyScan()
    {
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException("USERNAME_NOT_OCCUPIED"));
        client.Contacts_Search(Arg.Any<string>(), Arg.Any<int>()).ThrowsAsync(new InvalidOperationException("SEARCH_FAILED"));

        var result = await GateScanAsync();

        await _users.DidNotReceiveWithAnyArgs().ExcludeFromProfileScanAsync(default, default);
        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task FullProfileNotFetched_RunsNameOnlyScan()
    {
        var client = ResolvingClient();
        client.Users_GetFullUser(Arg.Any<InputUserBase>()).ThrowsAsync(new InvalidOperationException("USER_ID_INVALID"));

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task Timeout_RunsNameOnlyScan()
    {
        _sut = NewSut(TimeSpan.FromMilliseconds(100));
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>())
            .Returns(new TaskCompletionSource<Contacts_ResolvedPeer>().Task); // a hung DC connection

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    [Test]
    public async Task FloodWait_RunsNameOnlyScan()
    {
        var client = Client();
        client.Contacts_ResolveUsername(Arg.Any<string>())
            .ThrowsAsync(new TelegramFloodWaitException(30, DateTimeOffset.UtcNow.AddSeconds(30)));

        var result = await GateScanAsync();

        await AssertNameOnlyScanRanAsync(result);
    }

    // ── No trigger ──

    [Test]
    public async Task RuleShortCircuit_NoNameOnlyScan()
    {
        ResolvingClient();
        _scoring.ScoreAsync(default!, default!, default, default, default, default)
            .ReturnsForAnyArgs(new ScoringResult(5.0m, ProfileScanOutcome.Banned, 5.0m, 0m,
                "Rule-based detection triggered ban threshold", null));

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.Source, Is.EqualTo(ProfileScanSource.FullScan));
        await _results.Received(1).InsertAsync(
            Arg.Is<ProfileScanResultRecord>(r => r!.Source == ProfileScanSource.FullScan), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Bot_NoNameOnlyScan()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow(isBot: true));
        NoSession();

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Does.Contain("No User API session"));
    }

    [Test]
    public async Task ManualRescan_ExcludedUser_NoSession_RunsNameOnlyScan()
    {
        // The manual rescan (UserDetailDialog) calls the service directly with no chat; it always
        // runs, whatever the exclude flag says, and falls back like every other scan.
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow(excluded: true));
        _sessions.GetAnyClientAsync(Arg.Any<CancellationToken>()).Returns((IWTelegramApiClient?)null);

        var result = await _sut.ScanUserProfileAsync(Named, null, CancellationToken.None);

        await AssertNameOnlyScanRanAsync(result);
        await _users.DidNotReceiveWithAnyArgs().IncludeInProfileScanAsync(default, default);
    }

    [Test]
    public async Task SuccessfulFullScan_DoesNotClearAdminExclusion()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(StoredRow(excluded: true));
        ResolvingClient();

        var result = await GateScanAsync();

        Assert.That(result.Source, Is.EqualTo(ProfileScanSource.FullScan));
        await _users.DidNotReceiveWithAnyArgs().IncludeInProfileScanAsync(default, default);
        await _users.DidNotReceiveWithAnyArgs().ExcludeFromProfileScanAsync(default, default);
    }

    [Test]
    public async Task Fallback_UserWithoutNames_SkipsNameOnlyScan()
    {
        var idOnly = UserIdentity.ForTest(UserId);
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(idOnly);
        NoSession();

        var result = await GateScanAsync(idOnly);

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Is.Not.Null);
    }

    [Test]
    public async Task Fallback_NoStoredRow_SkipsNameOnlyScan()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((TelegramUser?)null);
        NoSession();

        var result = await GateScanAsync();

        await _scoring.DidNotReceiveWithAnyArgs().ScoreNameOnlyAsync(default!, default, default, default);
        Assert.That(result.SkipReason, Is.Not.Null);
    }

    // ── Failure: nothing written ──

    [Test]
    public async Task NameOnlyScanReturnsNoVerdict_WritesNothingAndLogsWarning()
    {
        _scoring.ScoreNameOnlyAsync(default!, default, default, default).ReturnsForAnyArgs((ScoringResult?)null);
        NoSession();

        var result = await GateScanAsync();

        await _results.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
        await _users.DidNotReceiveWithAnyArgs().UpdateProfileScanScoreAsync(default, default, default);
        await _moderation.DidNotReceiveWithAnyArgs().BanUserAsync(default!, default);
        await _reports.DidNotReceiveWithAnyArgs().InsertProfileScanAlertAsync(default!, default);
        Assert.That(result.SkipReason, Does.Contain("No User API session"));
        Assert.That(_logs.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("Name-only")));
    }

    // ── Outcome goes through the existing moderation path ──

    [Test]
    public async Task NameOnlyHeldForReview_RaisesProfileScanAlert()
    {
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(3.0m, ProfileScanOutcome.HeldForReview));
        NoSession();

        await GateScanAsync();

        await _reports.Received(1).InsertProfileScanAlertAsync(
            Arg.Is<ProfileScanAlertRecord>(a => a!.User == Named && a.Score == 3.0m && a.Outcome == ProfileScanOutcome.HeldForReview),
            Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendProfileScanAlertAsync(
            Chat, Named, 3.0m, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _moderation.DidNotReceiveWithAnyArgs().BanUserAsync(default!, default);
    }

    [Test]
    public async Task NameOnlyBanned_BansWithIdentityResolvedAfterTheScan()
    {
        var resolvedAfter = UserIdentity.ForTest(UserId, "Sam", "Rivera", "sam_rivera", NameVerdict.Clean);
        _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>()).Returns(resolvedAfter);
        _scoring.ScoreNameOnlyAsync(default!, default, default, default)
            .ReturnsForAnyArgs(NameOnlyScore(4.8m, ProfileScanOutcome.Banned));
        NoSession();

        await GateScanAsync();

        Received.InOrder(() =>
        {
            _results.InsertAsync(Arg.Any<ProfileScanResultRecord>(), Arg.Any<CancellationToken>());
            _identities.ResolveAsync(UserId, Arg.Any<CancellationToken>());
            _moderation.BanUserAsync(Arg.Any<BanIntent>(), Arg.Any<CancellationToken>());
        });
        await _moderation.Received(1).BanUserAsync(
            Arg.Is<BanIntent>(i => i!.User == resolvedAfter && i.Chat == Chat && i.Executor == Actor.ProfileScan),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PerChatOverride_OfNameOnlyThreshold_IsHonoured()
    {
        _config.GetEffectiveWelcomeAsync(ChatId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(new WelcomeConfig
            {
                JoinSecurity = new JoinSecurityConfig
                {
                    ProfileScan = new ProfileScanConfig { Enabled = true, NotifyThreshold = 1.5m, NameOnlyBanThreshold = 3.0m }
                }
            }));
        NoSession();

        await GateScanAsync();

        await _scoring.Received(1).ScoreNameOnlyAsync(Arg.Any<UserIdentity>(), 3.0m, 1.5m, Arg.Any<CancellationToken>());
    }

    // ── A name-only verdict is never reused by a later scan ──

    [Test]
    public async Task LatestScanNameOnly_RecentlyScanned_DoesNotReuseCachedScore()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow, score: 4.2m));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.NameOnly);
        NoSession();

        await GateScanAsync();

        await _sessions.Received(1).GetClientForChatAsync(ChatId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LatestScanFullScan_RecentlyScanned_ReusesCachedScore()
    {
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow, score: 1.0m));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.FullScan);

        await GateScanAsync();

        await _sessions.DidNotReceiveWithAnyArgs().GetClientForChatAsync(default, default);
    }

    [Test]
    public async Task LatestScanNameOnly_ProfileUnchanged_IsFullyRescored()
    {
        // An empty profile reads back exactly as stored, so the diff alone would reuse the name-only score.
        _users.GetByTelegramIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(StoredRow(scannedAt: DateTimeOffset.UtcNow.AddMinutes(-5), score: 1.0m, personalChannelId: 0));
        _results.GetLatestSourceAsync(UserId, Arg.Any<CancellationToken>()).Returns(ProfileScanSource.NameOnly);
        ResolvingClient();

        await GateScanAsync();

        await _scoring.ReceivedWithAnyArgs(1).ScoreAsync(default!, default!, default, default, default, default);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
