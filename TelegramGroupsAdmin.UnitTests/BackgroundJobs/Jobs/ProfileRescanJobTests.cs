using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using TelegramGroupsAdmin.BackgroundJobs.Jobs;
using TelegramGroupsAdmin.BackgroundJobs.Metrics;
using TelegramGroupsAdmin.BackgroundJobs.Services;
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

        _job = new ProfileRescanJob(NullLogger<ProfileRescanJob>.Instance, _jobConfig, _config,
            _users, _scanner, _identities, new JobMetrics());
    }

    private static WelcomeConfig Welcome(bool profileScanEnabled) =>
        new() { JoinSecurity = new JoinSecurityConfig { ProfileScan = new ProfileScanConfig { Enabled = profileScanEnabled } } };

    private static IJobExecutionContext Context()
    {
        var context = Substitute.For<IJobExecutionContext>();
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }

    private void Batch(params UserIdentity[] users)
    {
        _users.GetEligibleUsersForRescanAsync(Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(users.Select(u => u.Id).ToList());
        _identities.ResolveManyAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(users);
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
    public async Task Execute_PassesBatchSizeRetryCutoffAndRetryLimitFromSettings()
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

        await _users.Received(1).GetEligibleUsersForRescanAsync(
            25,
            Arg.Is<DateTimeOffset>(c => c <= start.AddDays(-2).AddSeconds(5) && c >= start.AddDays(-2).AddSeconds(-5)),
            5,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_DefaultSettings_UseRetryLimitThree()
    {
        Batch();

        await _job.Execute(Context());

        await _users.Received(1).GetEligibleUsersForRescanAsync(100, Arg.Any<DateTimeOffset>(), 3, Arg.Any<CancellationToken>());
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

    [Test]
    public void ProfileRescanSettings_StoredJsonWithoutRetryLimit_DefaultsToThree()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<ProfileRescanSettings>("""{"BatchSize":50,"RescanAfter":"1w"}""")!;

        Assert.That(settings.NameOnlyRetryLimit, Is.EqualTo(3));
    }
}
