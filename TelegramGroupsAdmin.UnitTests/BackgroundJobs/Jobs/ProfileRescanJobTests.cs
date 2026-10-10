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
    private IProfileScanGate _gate = null!;
    private IUserIdentityService _identities = null!;
    private IBackgroundJobConfigService _jobConfig = null!;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessions = null!;
#pragma warning restore NUnit1032
    private IChatService _chat = null!;
    private CapturingLogger<ProfileRescanJob> _logger = null!;
    private JobMetrics _metrics = null!;
    private readonly List<System.Diagnostics.Metrics.MeterListener> _listeners = [];
    private ProfileRescanJob _job = null!;

    [SetUp]
    public void SetUp()
    {
        _users = Substitute.For<ITelegramUserRepository>();
        _users.GetChatsForUserAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new List<ChatIdentity>());
        _gate = Substitute.For<IProfileScanGate>();
        _gate.IsScanningEnabledAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(true);
        _gate.ScanIfEligibleAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(ci => new ProfileScanResult(ci.Arg<UserIdentity>().Id, null, null, null, null, false, null,
                false, false, false, 0m, ProfileScanOutcome.Clean, null, null));
        _identities = Substitute.For<IUserIdentityService>();
        _jobConfig = Substitute.For<IBackgroundJobConfigService>();

        _sessions = Substitute.For<ITelegramSessionManager>();
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);
        _chat = Substitute.For<IChatService>();
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(true);
        _logger = new CapturingLogger<ProfileRescanJob>();
        _metrics = new JobMetrics();

        _job = NewJob(TimeSpan.Zero);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var listener in _listeners)
            listener.Dispose();
        _listeners.Clear();
    }

    // The 1s production throttle would make every scanning test slow; tests that pin it pass their own.
    private ProfileRescanJob NewJob(TimeSpan scanThrottle) =>
        new(_logger, _jobConfig, _users, _gate, _identities, _sessions, _chat, _metrics)
        {
            ScanThrottle = scanThrottle
        };

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
        _users.GetUsersWithIncompleteScansAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
        await _gate.Received(1).ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.Received(1).ScanIfEligibleAsync(eight, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Test]
    public async Task Execute_ScansThroughTheGateWithTheRescanTrigger()
    {
        // The gate decides eligibility like for every other automatic scan; the rescan trigger keeps the
        // "rescan" scan-source metric and skips only the already-scanned rule.
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        var chat = ChatIdentity.FromId(-1002);
        Chats(7, chat);

        await _job.Execute(Context());

        await _gate.Received(1).ScanIfEligibleAsync(seven, chat, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), false);
    }

    [Test]
    public async Task Execute_GateTurnsTheUserDown_DoesNotUseABatchSlot()
    {
        // A user the gate turns down (trusted or promoted mid-run, an admin, banned, a bot, excluded)
        // is not scanned and the slot goes to the next candidate.
        BatchSize(1);
        var turnedDown = UserIdentity.ForTest(7, "Turned Down");
        var next = UserIdentity.ForTest(8, "Next");
        Batch(turnedDown, next);
        _gate.ScanIfEligibleAsync(turnedDown, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns((ProfileScanResult?)null);

        await _job.Execute(Context());

        await _gate.Received(1).ScanIfEligibleAsync(next, null, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        Assert.That(_logger.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Information && e.Message == "Profile rescan: skipped 1 users the scan gate turned down"));
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
        await _users.Received(1).GetUsersWithIncompleteScansAsync(
            250,
            Arg.Is<DateTimeOffset>(c => c <= start.AddDays(-2).AddSeconds(5) && c >= start.AddDays(-2).AddSeconds(-5)),
            5,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_NoCandidates_LogsNothingToRetry()
    {
        Batch();

        await _job.Execute(Context());

        Assert.That(_logger.Entries, Has.One.EqualTo((LogLevel.Information, "Profile rescan: no incomplete scans to retry")));
    }

    [TestCase(0)]
    [TestCase(-2)]
    public async Task Execute_StoredRetryLimitBelowOne_UsesOne(int stored)
    {
        // The settings form allows 1 or more; a hand-edited or older stored value below that must not
        // stop every retry (or select nothing at all).
        _jobConfig.GetJobConfigAsync(BackgroundJobNames.ProfileRescan, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJobConfig
            {
                JobName = BackgroundJobNames.ProfileRescan,
                DisplayName = "Profile Rescan",
                Description = "test",
                Schedule = "every 6 hours",
                ProfileRescan = new ProfileRescanSettings { NameOnlyRetryLimit = stored }
            });
        Batch();

        await _job.Execute(Context());

        await _users.Received(1).GetUsersWithIncompleteScansAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), 1, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Execute_CandidateQueryThrows_RethrowsAndRecordsFailure()
    {
        var statuses = ListenToJobStatuses();
        _users.GetUsersWithIncompleteScansAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<List<long>>(_ => throw new InvalidOperationException("db down"));

        Assert.That(async () => await _job.Execute(Context()), Throws.InvalidOperationException);
        Assert.Multiple(() =>
        {
            Assert.That(statuses, Is.EqualTo(new object?[] { "failure" }));
            Assert.That(_logger.Entries, Has.One.EqualTo((LogLevel.Error, "Profile rescan batch failed")));
        });
    }

    [Test]
    public async Task Execute_NoSessionAndNoProfileScanAi_RecordsSuccess()
    {
        // Ending the run early is a decision, not a failure.
        var statuses = ListenToJobStatuses();
        _sessions.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(false);
        _chat.IsFeatureAvailableAsync(AIFeatureType.ProfileScan, Arg.Any<CancellationToken>()).Returns(false);

        await _job.Execute(Context());

        Assert.That(statuses, Is.EqualTo(new object?[] { "success" }));
    }

    /// <summary>The status tag of every job execution this test's JobMetrics records.</summary>
    private ConcurrentQueue<object?> ListenToJobStatuses()
    {
        var statuses = new ConcurrentQueue<object?>();
        var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, _metrics.Meter) && instrument.Name == "tga.jobs.executions_total")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "status")
                    statuses.Enqueue(tag.Value);
        });
        listener.Start();
        _listeners.Add(listener);
        return statuses;
    }

    [Test]
    public async Task Execute_DefaultSettings_UseRetryLimitThree()
    {
        Batch();

        await _job.Execute(Context());

        await _users.Received(1).GetUsersWithIncompleteScansAsync(1000, Arg.Any<DateTimeOffset>(), 3, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_SkippedScan_DoesNotAbortTheBatch()
    {
        // With no User API session every scan is name-only; one that writes nothing (skip reason
        // set) must not stop the rest of the batch.
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(new ProfileScanResult(7, null, null, null, null, false, null, false, false, false, 0m,
                ProfileScanOutcome.Clean, null, null,
                SkipReason: "No User API session available: none is connected, or it may be reconnecting. Sessions are connected from Profile > Telegram User API."));

        await _job.Execute(Context());

        await _gate.Received(1).ScanIfEligibleAsync(eight, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    private static ProfileScanResult NothingWritten(long userId) =>
        new(userId, null, null, null, null, false, null, false, false, false, 0m,
            ProfileScanOutcome.Clean, null, null, SkipReason: "Could not fetch the user's full profile.");

    [Test]
    public async Task Execute_ScanWritesNothing_RecordsTheAttempt()
    {
        // A scan that wrote nothing leaves the user never scanned; the attempt is recorded so the
        // next runs wait for Re-Scan After instead of retrying the user first every time.
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(NothingWritten(7));

        await _job.Execute(Context());

        await _users.Received(1).RecordScanAttemptAsync(7, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScanThrows_RecordsTheAttempt()
    {
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns<ProfileScanResult?>(_ => throw new InvalidOperationException("scan failed"));

        await _job.Execute(Context());

        await _users.Received(1).RecordScanAttemptAsync(7, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ChatLookupThrows_RecordsTheAttempt()
    {
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        _users.GetChatsForUserAsync(7, Arg.Any<CancellationToken>())
            .Returns<List<ChatIdentity>>(_ => throw new InvalidOperationException("db down"));

        await _job.Execute(Context());

        await _users.Received(1).RecordScanAttemptAsync(7, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_ScanWrites_DoesNotRecordAnAttempt()
    {
        Batch(UserIdentity.ForTest(7, "Seven"));

        await _job.Execute(Context());

        await _users.DidNotReceiveWithAnyArgs().RecordScanAttemptAsync(default);
    }

    [Test]
    public async Task Execute_GateTurnsTheUserDown_DoesNotRecordAnAttempt()
    {
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns((ProfileScanResult?)null);

        await _job.Execute(Context());

        await _users.DidNotReceiveWithAnyArgs().RecordScanAttemptAsync(default);
    }

    [Test]
    public async Task Execute_RecordingTheAttemptThrows_DoesNotAbortTheBatch()
    {
        // Recording the attempt runs after the scan; failing to record it must not fail the run.
        var seven = UserIdentity.ForTest(7, "Seven");
        var eight = UserIdentity.ForTest(8, "Eight");
        Batch(seven, eight);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(NothingWritten(7));
        _users.RecordScanAttemptAsync(7, Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("db down"));

        await _job.Execute(Context());

        await _gate.Received(1).ScanIfEligibleAsync(eight, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        Assert.That(_logger.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Warning && e.Message.Contains("could not record the scan attempt for user 7")));
    }

    private void Chats(long userId, params ChatIdentity[] chats) =>
        _users.GetChatsForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(chats.ToList());

    private void ProfileScan(long chatId, bool enabled) =>
        _gate.IsScanningEnabledAsync(chatId, Arg.Any<CancellationToken>()).Returns(enabled);

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

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
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

        await _gate.Received(1).ScanIfEligibleAsync(user, enabledChat, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.DidNotReceive().ScanIfEligibleAsync(user, disabledChat, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Execute_NeverPosted_GlobalConfigDecides(bool globallyEnabled)
    {
        var user = UserIdentity.ForTest(7, "No Chat");
        Batch(user);
        Chats(7);
        _users.HasMessageHistoryAsync(7, Arg.Any<CancellationToken>()).Returns(false);
        ProfileScan(0, globallyEnabled);

        await _job.Execute(Context());

        await _gate.Received(globallyEnabled ? 1 : 0).ScanIfEligibleAsync(user, null, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Test]
    public async Task Execute_PostedOnlyInChatsNoLongerManaged_IsSkippedWithoutUsingASlot()
    {
        // Message history, but none in an active managed chat: no longer a user, so no scan at all
        // (not even by the global config), and the batch slot goes to the next candidate.
        BatchSize(1);
        var gone = UserIdentity.ForTest(7, "Gone");
        var next = UserIdentity.ForTest(8, "Next");
        Batch(gone, next);
        Chats(7);
        _users.HasMessageHistoryAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        ProfileScan(0, enabled: true);

        await _job.Execute(Context());

        await _gate.DidNotReceive().ScanIfEligibleAsync(gone, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.Received(1).ScanIfEligibleAsync(next, null, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
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

        await _gate.Received(1).ScanIfEligibleAsync(users[3], enabledChat, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.Received(1).ScanIfEligibleAsync(users[4], enabledChat, ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.DidNotReceive().ScanIfEligibleAsync(users[5], Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await _gate.ReceivedWithAnyArgs(2).ScanIfEligibleAsync(default!, default, default, default);
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
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
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

        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
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

        await _gate.Received(1).ScanIfEligibleAsync(eight, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
        // Lower bound only (a delay never ends early); 50 ms of slack for timer granularity.
        Assert.That(nextScanAt - failedAt, Is.GreaterThanOrEqualTo(throttle - TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public void Execute_CancelledDuringTheThrottle_LogsCancelledNotFailed()
    {
        // Shutdown cancels the run mid-throttle: it ends with an Information line, not an Error.
        using var cts = new CancellationTokenSource();
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(cts.Token);
        _job = NewJob(TimeSpan.FromMinutes(1));
        var seven = UserIdentity.ForTest(7, "Seven");
        Batch(seven);
        _gate.ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), Arg.Any<ProfileScanTrigger>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new ProfileScanResult(7, null, null, null, null, false, null, false, false, false, 0m,
                    ProfileScanOutcome.Clean, null, null);
            });

        Assert.That(async () => await _job.Execute(context), Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(_logger.Entries.Count(e => e.Level == LogLevel.Information && e.Message == "Profile rescan cancelled"),
                Is.EqualTo(1));
            Assert.That(_logger.Entries.Any(e => e.Level == LogLevel.Error), Is.False);
        });
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

        await _users.DidNotReceiveWithAnyArgs().GetUsersWithIncompleteScansAsync(default, default, default, default);
        await _gate.DidNotReceiveWithAnyArgs().ScanIfEligibleAsync(default!, default, default, default);
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

        await _gate.Received(1).ScanIfEligibleAsync(seven, Arg.Any<ChatIdentity?>(), ProfileScanTrigger.Rescan, Arg.Any<CancellationToken>(), Arg.Any<bool>());
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
