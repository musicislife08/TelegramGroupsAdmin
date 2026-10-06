using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.AI.Services;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
using TelegramGroupsAdmin.Configuration.Models;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Models.BackgroundJobSettings;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.BackgroundJobs.Jobs;

[TestFixture]
public class ProfileRescanJobTests
{
    private ITelegramUserRepository _users = null!;
    private IProfileScanService _scanner = null!;
    private IUserIdentityService _identities = null!;
    private IBackgroundJobConfigService _jobConfig = null!;
    private IConfigService _config = null!;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private IChatService _chat = null!;
    private CapturingLogger<ProfileRescanJob> _logger = null!;
    private ProfileRescanJob _job = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<ITelegramUserRepository>();
        _users.GetChatsForUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new List<ChatIdentity>());
        _scanner = Substitute.For<IProfileScanService>();
        _scanner.ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ci => new ProfileScanResult(ci.Arg<UserIdentity>().Id, null, null, null, null, false, null,
                false, false, false, 0m, ProfileScanOutcome.Clean, null, null));
        _identities = Substitute.For<IUserIdentityService>();
        _jobConfig = Substitute.For<IBackgroundJobConfigService>();
        _config = Substitute.For<IConfigService>();
        _config.GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Welcome(profileScanEnabled: true));

        _sessions = Substitute.For<ITelegramSessionManager>();
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);
        _chat = Substitute.For<IChatService>();
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        _logger = new CapturingLogger<ProfileRescanJob>();

        _job = NewJob(TimeSpan.Zero);
    }

    // The 1s production throttle would make every scanning test slow; tests that pin it pass their own.
    private ProfileRescanJob NewJob(TimeSpan scanThrottle) =>
        new(_logger, _jobConfig, _config, _users, _scanner, _identities, _sessions, _chat, new JobMetrics())
        {
            ScanThrottle = scanThrottle
        };

    private static WelcomeConfig Welcome(bool profileScanEnabled) =>
        new() { JoinSecurity = new JoinSecurityConfig { ProfileScan = new ProfileScanConfig { Enabled = profileScanEnabled } } };

    private static IJobExecutionContext Context()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    // Like the repository, returns at most the requested number of candidates; identities come back
    // for the requested ids, in order.
    private void Batch(params UserIdentity[] users)
    {
        _users.GetEligibleUsersForRescanAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci => users.Take(ci.ArgAt<int>(0)).Select(u => u.Id).ToList());
        _identities.ResolveManyAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.ArgAt<IReadOnlyCollection<long>>(0).Select(id => users.Single(u => u.Id == id)).ToList());
    }

    [Test]
    public async Task Execute_ScansIdentitiesResolvedOnceForTheBatch()
    {
        // The batch's ids are resolved in one call; each scan gets that user's resolved identity.
        var seven = UserIdentity.ForTest(7, "Current Seven");
        var eight = UserIdentity.ForTest(8, "Current Eight");
        Batch(seven, eight);

        await _job.Execute(Context());

        await _identities.Received(1).ResolveManyAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>());
        await _identities.DidNotReceiveWithAnyArgs().ResolveAsync(default);
        await _scanner.Received(1).ScanUserProfileAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.Received(1).ScanUserProfileAsync(eight, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Test]
    public async Task Execute_PassesCandidateCapRetryCutoffAndRetryLimitFromSettings()
    {
        _jobConfig.GetJobConfigAsync(BackgroundJobNames.ProfileRescan, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ProfileRescan,
                DisplayName = "Profile Rescan",
                Description = "test",
                Schedule = "every 6 hours",
                ProfileRescan = new ProfileRescanSettings { BatchSize = 25, RescanAfter = "2d", NameOnlyRetryLimit = 5 }
            });
        Batch();
        var start = DateTimeOffset.UtcNow;

        await _job.Execute(Context());

        // Up to 10 x BatchSize candidates are examined, so users skipped for disabled scanning don't use up slots.
        await _users.Received(1).GetEligibleUsersForRescanAsync(
            250,
            Arg.Is<DateTimeOffset>(c => c <= start.AddDays(-2).AddSeconds(5) && c >= start.AddDays(-2).AddSeconds(-5)),
            5,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_DefaultSettings_UseRetryLimitThree()
    {
        Batch();

        await _job.Execute(Context());

        await _users.Received(1).GetEligibleUsersForRescanAsync(1000, Arg.Any<DateTimeOffset>(), 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_SkippedScan_DoesNotAbortTheBatch()
    {
        // With no User API session every scan is name-only; one that writes nothing (skip reason
        // set) must not stop the rest of the batch.
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        _scanner.ScanUserProfileAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(new ProfileScanResult(7, null, null, null, null, false, null, false, false, false, 0m,
                ProfileScanOutcome.Clean, null, null,
                SkipReason: "No User API session available. Connect a session in Settings."));

        await _job.Execute(Context());

        await _scanner.Received(1).ScanUserProfileAsync(eight, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    private void Chats(long userId, params ChatIdentity[] chats) =>
        _users.GetChatsForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(chats.ToList());

    private void ProfileScan(long chatId, bool enabled) =>
        _config.GetEffectiveWelcomeAsync(chatId, Arg.Any<CancellationToken>()).Returns(Welcome(enabled));

    [Test]
    public async Task Execute_MemberOnlyOfChatsWithScanningDisabled_IsSkipped()
    {
        // Scanning disabled in every chat the user is in means no scan at all, not even name-only.
        var user = UserIdentity.ForTest(7, "Disabled Only");
        Batch(user);
        Chats(7, ChatIdentity.FromId(-1001), ChatIdentity.FromId(-1003));
        ProfileScan(-1001, enabled: false);
        ProfileScan(-1003, enabled: false);

        await _job.Execute(Context());

        await _scanner.DidNotReceiveWithAnyArgs().ScanUserProfileAsync(default!, default, default, default);
    }

    [Test]
    public async Task Execute_MemberOfDisabledAndEnabledChats_IsScannedForTheEnabledChat()
    {
        // The most recently active chat has scanning disabled; the scan runs for the enabled one.
        var user = UserIdentity.ForTest(7, "Mixed Chats");
        Batch(user);
        var disabledChat = ChatIdentity.FromId(-1001);
        var enabledChat = ChatIdentity.FromId(-1002);
        Chats(7, disabledChat, enabledChat);
        ProfileScan(-1001, enabled: false);
        ProfileScan(-1002, enabled: true);

        await _job.Execute(Context());

        await _scanner.Received(1).ScanUserProfileAsync(user, enabledChat, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.DidNotReceive().ScanUserProfileAsync(user, disabledChat, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Execute_NoKnownChat_GlobalConfigDecides(bool globallyEnabled)
    {
        var user = UserIdentity.ForTest(7, "No Chat");
        Batch(user);
        Chats(7);
        ProfileScan(0, globallyEnabled);

        await _job.Execute(Context());

        await _scanner.Received(globallyEnabled ? 1 : 0)
            .ScanUserProfileAsync(user, null, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    private void BatchSize(int batchSize) =>
        _jobConfig.GetJobConfigAsync(BackgroundJobNames.ProfileRescan, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ProfileRescan,
                DisplayName = "Profile Rescan",
                Description = "test",
                Schedule = "every 6 hours",
                ProfileRescan = new ProfileRescanSettings { BatchSize = batchSize }
            });

    [Test]
    public async Task Execute_DisabledCandidatesFirst_StillScansBatchSizeEnabledUsers()
    {
        // Users skipped because every chat has scanning disabled don't use up batch slots.
        BatchSize(2);
        var disabledChat = ChatIdentity.FromId(-1001);
        var enabledChat = ChatIdentity.FromId(-1002);
        ProfileScan(-1001, enabled: false);
        ProfileScan(-1002, enabled: true);
        var users = Enumerable.Range(1, 6).Select(i => UserIdentity.ForTest(i, $"User {i}")).ToArray();
        Batch(users);
        foreach (var user in users.Take(3))
            Chats(user.Id, disabledChat);
        foreach (var user in users.Skip(3))
            Chats(user.Id, enabledChat);

        await _job.Execute(Context());

        await _scanner.Received(1).ScanUserProfileAsync(users[3], enabledChat, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.Received(1).ScanUserProfileAsync(users[4], enabledChat, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.DidNotReceive().ScanUserProfileAsync(users[5], Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.ReceivedWithAnyArgs(2).ScanUserProfileAsync(default!, default, default, default);
    }

    [Test]
    public async Task Execute_ExaminedCandidateCap_StopsTheRun()
    {
        // At most 10 x BatchSize candidates are examined per run: the job asks the repository for
        // that many, however many are eligible.
        BatchSize(1);
        var disabledChat = ChatIdentity.FromId(-1001);
        ProfileScan(-1001, enabled: false);
        var users = Enumerable.Range(1, 11).Select(i => UserIdentity.ForTest(i, $"User {i}")).ToArray();
        Batch(users);
        foreach (var user in users.Take(10))
            Chats(user.Id, disabledChat);

        await _job.Execute(Context());

        await _users.Received(10).GetChatsForUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _scanner.DidNotReceiveWithAnyArgs().ScanUserProfileAsync(default!, default, default, default);
    }

    [Test]
    public async Task Execute_ChatLookupThrows_UsesABatchSlot()
    {
        // A failed chat lookup is a failed attempt like a failed scan: it takes a batch slot.
        BatchSize(1);
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        _users.GetChatsForUserAsync(7, Arg.Any<CancellationToken>())
            .Returns<List<ChatIdentity>>(_ => throw new InvalidOperationException("db down"));

        await _job.Execute(Context());

        await _scanner.DidNotReceiveWithAnyArgs().ScanUserProfileAsync(default!, default, default, default);
        Assert.That(_logger.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("failed to scan user 7")));
    }

    [Test]
    public async Task Execute_ChatLookupThrows_IsThrottledLikeAScan()
    {
        var throttle = TimeSpan.FromMilliseconds(300);
        _job = NewJob(throttle);
        BatchSize(2);
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan failedAt = default, nextScanAt = default;
        _users.GetChatsForUserAsync(7, Arg.Any<CancellationToken>())
            .Returns<List<ChatIdentity>>(_ =>
            {
                failedAt = clock.Elapsed;
                throw new InvalidOperationException("db down");
            });
        _users.GetChatsForUserAsync(8, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                nextScanAt = clock.Elapsed;
                return new List<ChatIdentity>();
            });

        await _job.Execute(Context());

        await _scanner.Received(1).ScanUserProfileAsync(eight, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        // Lower bound only (a delay never ends early); 50 ms of slack for timer granularity.
        Assert.That(nextScanAt - failedAt, Is.GreaterThanOrEqualTo(throttle - TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public async Task Execute_UsersSharingAChat_ReadThatChatsConfigOnce()
    {
        var shared = ChatIdentity.FromId(-1002);
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        Chats(7, shared);
        Chats(8, shared);
        ProfileScan(-1002, enabled: true);

        await _job.Execute(Context());

        await _config.Received(1).GetEffectiveWelcomeAsync(-1002, Arg.Any<CancellationToken>());
        await _scanner.Received(1).ScanUserProfileAsync(seven, shared, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _scanner.Received(1).ScanUserProfileAsync(eight, shared, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Test]
    public void ProfileRescanSettings_StoredJsonWithoutRetryLimit_DefaultsToThree()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<ProfileRescanSettings>("""{"BatchSize":50,"RescanAfter":"1w"}""")!;

        Assert.That(settings.NameOnlyRetryLimit, Is.EqualTo(3));
    }

    private const string NothingCanScanWarning =
        "Profile rescan skipped: no User API session and the profile-scan AI is unavailable";

    [Test]
    public async Task Execute_NoSessionAndNoProfileScanAi_EndsTheRunWithOneWarning()
    {
        // Every attempt would fail outright and the same never-scanned users would take the batch
        // on every run, so the run ends before examining candidates.
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(false);
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(false);
        Batch(UserIdentity.ForTest(7, "Seven"));

        await _job.Execute(Context());

        await _users.DidNotReceiveWithAnyArgs().GetEligibleUsersForRescanAsync(default, default, default, default);
        await _scanner.DidNotReceiveWithAnyArgs().ScanUserProfileAsync(default!, default, default, default);
        Assert.That(_logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message == NothingCanScanWarning),
            Is.EqualTo(1));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Execute_SessionOrProfileScanAiAvailable_Runs(bool session, bool ai)
    {
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(session);
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(ai);
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);

        await _job.Execute(Context());

        await _scanner.Received(1).ScanUserProfileAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        Assert.That(_logger.Entries.Any(e => e.Message == NothingCanScanWarning), Is.False);
    }

    [Test]
    public async Task Execute_CandidatesExaminedButNoneAttempted_LogsOnceAtInformation()
    {
        var disabledChat = ChatIdentity.FromId(-1001);
        ProfileScan(-1001, enabled: false);
        var users = Enumerable.Range(1, 3).Select(i => UserIdentity.ForTest(i, $"User {i}")).ToArray();
        Batch(users);
        foreach (var user in users)
            Chats(user.Id, disabledChat);

        await _job.Execute(Context());

        Assert.That(_logger.Entries.Count(e => e.Level == LogLevel.Information
            && e.Message == "Profile rescan: examined 3 candidates but attempted no scans"), Is.EqualTo(1));
    }

    [Test]
    public async Task Execute_ScansAttempted_DoesNotLogTheNoAttemptsMessage()
    {
        Batch(UserIdentity.ForTest(7, "Seven"));

        await _job.Execute(Context());

        Assert.That(_logger.Entries.Any(e => e.Message.Contains("attempted no scans")), Is.False);
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
