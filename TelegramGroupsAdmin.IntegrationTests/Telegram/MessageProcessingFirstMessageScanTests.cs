using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramGroupsAdmin.Configuration;
using TelegramGroupsAdmin.Configuration.Models.Welcome;
using TelegramGroupsAdmin.Configuration.Repositories;
using TelegramGroupsAdmin.Configuration.Services;
using TelegramGroupsAdmin.ContentDetection.Repositories;
using TelegramGroupsAdmin.ContentDetection.Services;
using TelegramGroupsAdmin.Core.BackgroundJobs;
using TelegramGroupsAdmin.Core.Extensions;
using TelegramGroupsAdmin.Core.Models;
using TelegramGroupsAdmin.Core.Services;
using TelegramGroupsAdmin.Data;
using TelegramGroupsAdmin.IntegrationTests.TestData;
using TelegramGroupsAdmin.IntegrationTests.TestHelpers;
using TelegramGroupsAdmin.Telegram.Handlers;
using TelegramGroupsAdmin.Telegram.Metrics;
using TelegramGroupsAdmin.Telegram.Repositories;
using TelegramGroupsAdmin.Telegram.Services;
using TelegramGroupsAdmin.Telegram.Services.BackgroundServices;
using TelegramGroupsAdmin.Telegram.Services.Bot;
using TelegramGroupsAdmin.Telegram.Services.BotCommands;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Integration tests for the first-message profile-scan trigger wiring in
/// <see cref="MessageProcessingService.HandleNewMessageAsync"/>.
///
/// <para><b>The regression this covers.</b> An untrusted user with no prior
/// <c>telegram_users</c> row and no join event posts a message. The join trigger never
/// fired (no join event) and the profile-diff trigger never fired (no stored row to diff
/// against), so the account was never profile-scanned. <see cref="ProfileScanGate"/>'s own
/// decision logic is covered by unit tests; what is NOT covered anywhere else — and what
/// caused the outage — is whether <see cref="MessageProcessingService"/> actually reaches
/// the gate on the new-message path, with <see cref="ProfileScanTrigger.FirstMessage"/>.
/// </para>
///
/// <para><b>Harness strategy.</b> <c>HandleNewMessageAsync</c> resolves 24 distinct services,
/// seven of which are concrete handler classes whose non-virtual methods NSubstitute cannot
/// intercept. The heaviest of those, <c>ContentDetectionOrchestrator</c> (the whole detection
/// engine plus the AI stack), is kept out of the graph entirely by arranging the substituted
/// <see cref="IProfileScanService"/> to return <see cref="ProfileScanOutcome.Banned"/>: the
/// production code short-circuits content detection on a Banned first-message scan. The
/// remaining concrete handlers (<see cref="CommandRouter"/>, <see cref="ImageProcessingHandler"/>,
/// <see cref="MediaProcessingHandler"/>, <see cref="FileScanningHandler"/>,
/// <see cref="BackgroundJobScheduler"/>, <see cref="AdminMentionHandler"/>) are registered as
/// real instances — each takes only interface dependencies, and each returns early for a
/// plain-text, non-command message before touching them.
/// </para>
///
/// <para><b>Real vs substituted.</b> Real, against Testcontainers Postgres cloned from the
/// golden template: <see cref="ProfileScanGate"/>, <see cref="IConfigService"/>,
/// <see cref="ITelegramUserRepository"/>, <see cref="IMessageHistoryRepository"/>,
/// <see cref="IManagedChatsRepository"/>, <see cref="IChatAdminsRepository"/>,
/// <see cref="IUserActionsRepository"/>, <see cref="IUsernameHistoryRepository"/>.
/// Substituted: <see cref="IProfileScanService"/> (no live WTelegram API session is available,
/// so the scan itself cannot be real), <see cref="ITelegramSessionManager"/>, and every
/// remaining interface-based dependency. Asserting on the substituted
/// <see cref="IProfileScanService"/> is deliberate: there is no real-behaviour signal for
/// "a scan happened" without a live User API session.
/// </para>
///
/// <para>Canonical anchors: MainChat (<c>-100026957614982</c>, is_active=true) supplies the
/// chat. The acting user id is deliberately OUTSIDE the canonical telegram-user range
/// <c>[9_000_000_000_000, 10_000_000_000_000)</c> so it is guaranteed absent from
/// <c>telegram_users</c> — the precondition that defines the regression. Each test asserts
/// that precondition before acting.</para>
/// </summary>
[TestFixture]
public class MessageProcessingFirstMessageScanTests
{
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;

    // Outside the canonical range [9_000_000_000_000, 10_000_000_000_000), so this user
    // cannot collide with any of the 335 canonical telegram_users rows. Never seen before:
    // no join event, no prior scan, no stored profile to diff against.
    private const long UnscannedUserId = 4242424242L;

    private const int TestMessageId = 990001;

    private MigrationTestHelper _testHelper = null!;
    private ServiceProvider _serviceProvider = null!;
    private IMessageProcessingService _sut = null!;
    private IProfileScanService _profileScanService = null!;
#pragma warning disable NUnit1032 // Substitutes are not owned resources
    private ITelegramSessionManager _sessionManager = null!;
#pragma warning restore NUnit1032
    private string _dataPath = null!;

    [SetUp]
    public async Task SetUp()
    {
        _testHelper = new MigrationTestHelper();
        await _testHelper.CreateDatabaseFromGoldenTemplateAsync();

        _dataPath = Path.Combine(Path.GetTempPath(), $"tga-firstmsg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataPath);

        _profileScanService = Substitute.For<IProfileScanService>();
        _sessionManager = Substitute.For<ITelegramSessionManager>();

        // A Banned outcome is what keeps ContentDetectionOrchestrator out of the graph.
        _profileScanService
            .ScanUserProfileAsync(Arg.Any<UserIdentity>(), Arg.Any<ChatIdentity?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(BannedScanResult()));

        _sessionManager.HasAnyActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(true);

        var services = new ServiceCollection();

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(_testHelper.ConnectionString));

        services.AddLogging(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        services.Configure<AppOptions>(options => options.DataPath = _dataPath);

        services.AddCoreServices();
        services.AddHybridCache();
        services.AddDataProtection();

        // ── Real: repositories the profile-scan gate and the message path read/write ──
        services.AddScoped<ITelegramUserRepository, TelegramUserRepository>();
        services.AddScoped<IMessageHistoryRepository, MessageHistoryRepository>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();
        services.AddScoped<IUsernameHistoryRepository, UsernameHistoryRepository>();
        services.AddScoped<IManagedChatsRepository, ManagedChatsRepository>();
        services.AddScoped<IChatAdminsRepository, ChatAdminsRepository>();

        // ── Real: IConfigService, so the ScanOnFirstMessage flag round-trips through
        //    the same welcome_config JSONB path production reads. ──
        services.AddScoped<IConfigRepository, ConfigRepository>();
        services.AddScoped<IContentDetectionConfigRepository, ContentDetectionConfigRepository>();
        services.AddScoped<IConfigService, ConfigService>();

        // ── Real: the gate under test ──
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton<ChatMetrics>();
        services.AddScoped<IProfileScanGate, ProfileScanGate>();

        // ── Real: concrete handlers on the pre-detection path. All take interface-only
        //    dependencies and short-circuit for a plain-text, non-command message. ──
        services.AddSingleton<CommandRouter>();
        services.AddScoped<AdminMentionHandler>();
        services.AddScoped<ImageProcessingHandler>();
        services.AddScoped<TelegramMediaService>();
        services.AddScoped<MediaProcessingHandler>();
        services.AddScoped<FileScanningHandler>();
        services.AddScoped<BackgroundJobScheduler>();

        // ── Substituted: the scan itself (needs a live WTelegram User API session) ──
        services.AddSingleton(_profileScanService);
        services.AddSingleton(_sessionManager);

        // ── Substituted: everything else the path can touch ──
        services.AddSingleton(Substitute.For<IChatCache>());
        services.AddSingleton(Substitute.For<IJobScheduler>());
        services.AddSingleton(Substitute.For<ITranslationHandler>());
        services.AddSingleton(Substitute.For<IBotMediaService>());
        services.AddSingleton(Substitute.For<IBotMessageService>());
        services.AddSingleton(Substitute.For<IBotChatService>());
        services.AddSingleton(Substitute.For<IBotUserService>());
        services.AddSingleton(Substitute.For<IBotModerationService>());
        services.AddSingleton(Substitute.For<IMessageTranslationService>());
        services.AddSingleton(Substitute.For<IUrlContentScrapingService>());
        services.AddSingleton(Substitute.For<IExamFlowService>());
        services.AddSingleton(Substitute.For<IWelcomeResponsesRepository>());

        services.AddSingleton<IMessageProcessingService, MessageProcessingService>();

        _serviceProvider = services.BuildServiceProvider();

        // The translation handler is on the non-optional path: it must hand back the
        // original text so detection input is unchanged.
        _serviceProvider.GetRequiredService<ITranslationHandler>()
            .GetTextForDetectionAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(
                new TranslationForDetectionResult(callInfo.ArgAt<string?>(0) ?? "", null, null)));

        _sut = _serviceProvider.GetRequiredService<IMessageProcessingService>();
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
        _testHelper.Dispose();

        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    [Test]
    public async Task HandleNewMessage_UntrustedUserNeverScanned_TriggersProfileScan()
    {
        // Reproduces the outage: an untrusted user with no join event and no prior scan
        // posts a message. Before the fix, no trigger fired and this user was never scanned.
        await EnableFirstMessageScanAsync(scanOnFirstMessage: true);
        await AssertUserIsUnknownAsync();

        await _sut.HandleNewMessageAsync(CreateGroupMessage(), CancellationToken.None);

        await _profileScanService.Received(1).ScanUserProfileAsync(
            Arg.Is<UserIdentity>(u => u != null && u.Id == UnscannedUserId),
            Arg.Is<ChatIdentity?>(c => c != null && c.Id == MainChatId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleNewMessage_ScanOnFirstMessageDisabled_DoesNotScan()
    {
        // Proves the call site passes ProfileScanTrigger.FirstMessage and not some other
        // trigger: ScanOnJoin and ScanOnProfileChange stay enabled, only the first-message
        // flag is off, and the gate must decline.
        //
        // MainChat is deactivated so content detection is skipped by the inactive-chat
        // branch — without the Banned short-circuit, ContentDetectionOrchestrator would
        // otherwise be resolved and drag in the whole AI/detection stack.
        await EnableFirstMessageScanAsync(scanOnFirstMessage: false);
        await DeactivateMainChatAsync();
        await AssertUserIsUnknownAsync();

        await _sut.HandleNewMessageAsync(CreateGroupMessage(), CancellationToken.None);

        await _profileScanService.DidNotReceive().ScanUserProfileAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity?>(),
            Arg.Any<CancellationToken>());
    }

    // ── Arrange helpers ───────────────────────────────────────────────────────

    // ProfileScanConfig.ScanOnFirstMessage defaults to false, so the positive test has to
    // turn it on. Written through the real IConfigService for MainChat so the gate reads it
    // back off the same welcome_config JSONB column production uses.
    private async Task EnableFirstMessageScanAsync(bool scanOnFirstMessage)
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var chat = new ChatIdentity(MainChatId, "Main Community");

        var welcome = await configService.GetEffectiveWelcomeAsync(MainChatId)
                      ?? WelcomeConfig.Default;

        welcome.JoinSecurity.ProfileScan.Enabled = true;
        welcome.JoinSecurity.ProfileScan.ScanOnJoin = true;
        welcome.JoinSecurity.ProfileScan.ScanOnProfileChange = true;
        welcome.JoinSecurity.ProfileScan.ScanOnFirstMessage = scanOnFirstMessage;

        await configService.SaveWelcomeAsync(chat, welcome, Actor.SystemSeed);

        // Confirm the flag actually landed — a silently-unpersisted flag would make the
        // positive test fail for the wrong reason and the negative test pass vacuously.
        var effective = await configService.GetEffectiveWelcomeAsync(MainChatId);
        Assert.That(effective?.JoinSecurity.ProfileScan.Enabled, Is.True);
        Assert.That(effective?.JoinSecurity.ProfileScan.ScanOnFirstMessage, Is.EqualTo(scanOnFirstMessage));
    }

    // The precondition that defines the regression: this user has never been seen.
    private async Task AssertUserIsUnknownAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var userRepository = scope.ServiceProvider.GetRequiredService<ITelegramUserRepository>();

        var existing = await userRepository.GetByTelegramIdAsync(UnscannedUserId);
        Assert.That(existing, Is.Null,
            "The acting user must have no telegram_users row for this to reproduce the outage.");
    }

    private async Task DeactivateMainChatAsync()
    {
        await using var context = _testHelper.GetDbContext();
        var chat = await context.ManagedChats.FirstAsync(mc => mc.ChatId == MainChatId);
        chat.IsActive = false;
        await context.SaveChangesAsync();
    }

    private static Message CreateGroupMessage() => new()
    {
        Id = TestMessageId,
        Date = DateTime.UtcNow,
        Chat = TelegramTestFactory.CreateChat(MainChatId, ChatType.Supergroup, "Main Community"),
        From = TelegramTestFactory.CreateUser(UnscannedUserId, "Andrea", username: "AndreaRuiz83"),
        Text = "hi"
    };

    private static ProfileScanResult BannedScanResult() => new(
        TelegramUserId: UnscannedUserId,
        Bio: "Crypto signals, DM me",
        PersonalChannelId: null,
        PersonalChannelTitle: null,
        PersonalChannelAbout: null,
        HasPinnedStories: false,
        PinnedStoryCaptions: null,
        IsScam: true,
        IsFake: false,
        IsVerified: false,
        Score: 5.0m,
        Outcome: ProfileScanOutcome.Banned,
        AiReason: "Crypto solicitation in bio",
        AiSignalsDetected: ["crypto_solicitation"]);
}
