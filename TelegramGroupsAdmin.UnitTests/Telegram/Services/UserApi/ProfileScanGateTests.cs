using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Models;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.UnitTests.Telegram.Services.UserApi;

/// <summary>
/// Unit tests for ProfileScanGate, the single owner of the profile scan
/// eligibility decision shared by the join, first-message, and profile-change
/// triggers.
/// </summary>
[TestFixture]
public class ProfileScanGateTests
{
    private const long TestUserId = 8872589479L;
    private const long TestChatId = -1001329174109L;

    private IConfigService _configService = null!;
    private ITelegramUserRepository _userRepository = null!;
#pragma warning disable NUnit1032 // Mock doesn't need disposal
    private ITelegramSessionManager _sessionManager = null!;
#pragma warning restore NUnit1032
    private IProfileScanService _profileScanService = null!;
    private ProfileScanGate _gate = null!;

    [SetUp]
    public void SetUp()
    {
        _configService = Substitute.For<IConfigService>();
        _userRepository = Substitute.For<ITelegramUserRepository>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();
        _profileScanService = Substitute.For<IProfileScanService>();

        // Defaults: everything enabled, session active, scan returns Clean.
        SetConfig(CreateConfig());
        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);
        _profileScanService
            .ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>())
            .Returns(CreateScanResult());

        _gate = new ProfileScanGate(
            _configService,
            _userRepository,
            _sessionManager,
            _profileScanService,
            new PipelineMetrics(),
            NullLogger<ProfileScanGate>.Instance);
    }

    [Test]
    public async Task FirstMessage_UntrustedNeverScanned_Scans()
    {
        // The regression: an untrusted user whose first observed activity is a
        // message, with no join event, was never scanned by any trigger.
        SetUser(CreateUser(profileScannedAt: null, isTrusted: false));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task FirstMessage_UserRowDoesNotExistYet_Scans()
    {
        SetUser(null);

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task FirstMessage_AlreadyScanned_Skips()
    {
        SetUser(CreateUser(profileScannedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FirstMessage_TrustedUser_Skips()
    {
        SetUser(CreateUser(profileScannedAt: null, isTrusted: true));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FirstMessage_FlagDisabled_Skips()
    {
        var config = CreateConfig();
        config.ScanOnFirstMessage = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Join_AlreadyScanned_StillScans()
    {
        // Join always rescans. Only the first-message trigger is once-per-user.
        SetUser(CreateUser(profileScannedAt: DateTimeOffset.UtcNow.AddDays(-3)));

        var result = await ScanAsync(ProfileScanTrigger.Join);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task Join_ScanOnJoinDisabled_Skips()
    {
        var config = CreateConfig();
        config.ScanOnJoin = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.Join);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ProfileChange_ScanOnProfileChangeDisabled_Skips()
    {
        // Proves the previously dead ScanOnProfileChange flag is now honored.
        var config = CreateConfig();
        config.ScanOnProfileChange = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        var result = await ScanAsync(ProfileScanTrigger.ProfileChange);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ProfileScanDisabled_AllTriggersSkip()
    {
        var config = CreateConfig();
        config.Enabled = false;
        SetConfig(config);
        SetUser(CreateUser(profileScannedAt: null));

        Assert.Multiple(async () =>
        {
            Assert.That(await ScanAsync(ProfileScanTrigger.Join), Is.Null);
            Assert.That(await ScanAsync(ProfileScanTrigger.FirstMessage), Is.Null);
            Assert.That(await ScanAsync(ProfileScanTrigger.ProfileChange), Is.Null);
        });
    }

    [Test]
    public async Task NoActiveSession_Skips()
    {
        SetUser(CreateUser(profileScannedAt: null));
        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await ScanAsync(ProfileScanTrigger.FirstMessage);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void ScanThrows_ExceptionPropagates()
    {
        SetUser(CreateUser(profileScannedAt: null));
        _profileScanService
            .ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProfileScanResult>>(_ => throw new InvalidOperationException("scan failed"));

        Assert.That(
            async () => await ScanAsync(ProfileScanTrigger.FirstMessage),
            Throws.TypeOf<InvalidOperationException>());
    }

    private Task<ProfileScanResult?> ScanAsync(ProfileScanTrigger trigger) =>
        _gate.ScanIfEligibleAsync(
            UserIdentity.FromId(TestUserId),
            ChatIdentity.FromId(TestChatId),
            trigger,
            CancellationToken.None);

    private void SetConfig(ProfileScanConfig profileScan)
    {
        var welcome = WelcomeConfig.Default;
        welcome.JoinSecurity.ProfileScan = profileScan;
        _configService
            .GetEffectiveWelcomeAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<WelcomeConfig?>(welcome));
    }

    private void SetUser(TelegramUser? user) =>
        _userRepository
            .GetByTelegramIdAsync(TestUserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(user));

    private static ProfileScanConfig CreateConfig() => new()
    {
        Enabled = true,
        ScanOnJoin = true,
        ScanOnProfileChange = true,
        ScanOnFirstMessage = true,
    };

    private static ProfileScanResult CreateScanResult() => new(
        TelegramUserId: TestUserId,
        Bio: null,
        PersonalChannelId: null,
        PersonalChannelTitle: null,
        PersonalChannelAbout: null,
        HasPinnedStories: false,
        PinnedStoryCaptions: null,
        IsScam: false,
        IsFake: false,
        IsVerified: false,
        Score: 0.0m,
        Outcome: ProfileScanOutcome.Clean,
        AiReason: null,
        AiSignalsDetected: null);

    private static TelegramUser CreateUser(
        DateTimeOffset? profileScannedAt,
        bool isTrusted = false)
    {
        var now = DateTimeOffset.UtcNow;
        return new TelegramUser(
            TelegramUserId: TestUserId,
            Username: "AndreaRuiz83",
            FirstName: "Andrea",
            LastName: null,
            UserPhotoPath: null, PhotoHash: null, PhotoFileUniqueId: null,
            IsBot: false, IsTrusted: isTrusted, IsBanned: false,
            KickCount: 0, BotDmEnabled: false,
            FirstSeenAt: now, LastSeenAt: now, CreatedAt: now, UpdatedAt: now,
            ProfileScannedAt: profileScannedAt);
    }
}
