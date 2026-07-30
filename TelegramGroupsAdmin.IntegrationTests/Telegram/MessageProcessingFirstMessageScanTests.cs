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
/// <para><b>READ BEFORE DELETING EITHER TEST: the two tests are a matched pair, and neither
/// proves correct-trigger wiring on its own.</b> <see cref="ProfileScanGate"/> does not forward
/// the trigger to <see cref="IProfileScanService.ScanUserProfileAsync"/>, so the substituted scan
/// service cannot observe <i>which</i> trigger caused a call — only that a call happened. Trigger
/// identity is therefore established by the two tests <i>jointly</i>, from opposite sides, via the
/// config flags each one enables:
/// <list type="bullet">
/// <item><description><see cref="HandleNewMessage_UntrustedUserNeverScanned_TriggersProfileScan"/>
/// enables <c>ScanOnFirstMessage</c> ONLY (join and profile-change off), so a call site using any
/// other trigger makes the gate decline and the expected call never arrives.</description></item>
/// <item><description><see cref="HandleNewMessage_ScanOnFirstMessageDisabled_DoesNotScan"/>
/// inverts it — join and profile-change on, <c>ScanOnFirstMessage</c> off — so a call site using
/// any other trigger produces an unexpected call.</description></item>
/// </list>
/// The narrowed flags in the positive test are what let it discriminate at all; before that
/// narrowing, mutating the production call site from <c>FirstMessage</c> to <c>Join</c> left the
/// positive test passing. Deleting either test as "redundant", or widening the flags either test
/// sets, silently gives up coverage of the exact property this fixture exists to guarantee.
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
        //
        // PAIRED WITH HandleNewMessage_ScanOnFirstMessageDisabled_DoesNotScan — see the
        // class doc. ScanOnFirstMessage is the ONLY trigger enabled here, which is what makes
        // this test discriminate on trigger identity: if the call site passed Join or
        // ProfileChange, the gate would decline and the expected call would never arrive.
        // Widening scanOnOtherTriggers to true would silently destroy that property.
        await ConfigureProfileScanAsync(scanOnFirstMessage: true, scanOnOtherTriggers: false);
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
        // The inverse half of the pair, PAIRED WITH
        // HandleNewMessage_UntrustedUserNeverScanned_TriggersProfileScan — see the class doc.
        // ScanOnJoin and ScanOnProfileChange stay enabled and only the first-message flag is
        // off, so any call site using a trigger other than FirstMessage produces a call that
        // must not happen.
        //
        // MainChat is deactivated so content detection is skipped by the inactive-chat
        // branch — with no scan there is no Banned short-circuit, and
        // ContentDetectionOrchestrator would otherwise be resolved and drag in the whole
        // AI/detection stack.
        await ConfigureProfileScanAsync(scanOnFirstMessage: false, scanOnOtherTriggers: true);
        await DeactivateMainChatAsync();
        await AssertUserIsUnknownAsync();

        await _sut.HandleNewMessageAsync(CreateGroupMessage(), CancellationToken.None);

        // Positive control. HandleNewMessageAsync wraps the whole group-message path in a
        // catch-all that only logs, so any unrelated failure upstream of the gate would also
        // present as "no scan received" and pass the assertion below for the wrong reason.
        // Message persistence happens earlier on the same path than the gate call, so a
        // persisted row proves the SUT actually ran far enough to reach the gate decision.
        await AssertMessageWasPersistedAsync();

        await _profileScanService.DidNotReceive().ScanUserProfileAsync(
            Arg.Any<UserIdentity>(),
            Arg.Any<ChatIdentity?>(),
            Arg.Any<CancellationToken>());
    }

    // ── Arrange helpers ───────────────────────────────────────────────────────

    // ProfileScanConfig.ScanOnFirstMessage defaults to false, so the positive test has to
    // turn it on. Written through the real IConfigService for MainChat so the gate reads it
    // back off the same welcome_config JSONB column production uses.
    //
    // The two flags are set in OPPOSITION by the two tests, and that opposition is what
    // establishes trigger identity (see the class doc). scanOnOtherTriggers drives both
    // ScanOnJoin and ScanOnProfileChange; neither of those code paths is reachable for this
    // message (no join event, and no stored user row to diff against), so their only effect
    // is on what the gate would admit if the call site named the wrong trigger.
    private async Task ConfigureProfileScanAsync(bool scanOnFirstMessage, bool scanOnOtherTriggers)
    {
        using var scope = _serviceProvider.CreateScope();
        var configService = scope.ServiceProvider.GetRequiredService<IConfigService>();
        var chat = new ChatIdentity(MainChatId, "Main Community");

        var welcome = await configService.GetEffectiveWelcomeAsync(MainChatId)
                      ?? WelcomeConfig.Default;

        welcome.JoinSecurity.ProfileScan.Enabled = true;
        welcome.JoinSecurity.ProfileScan.ScanOnJoin = scanOnOtherTriggers;
        welcome.JoinSecurity.ProfileScan.ScanOnProfileChange = scanOnOtherTriggers;
        welcome.JoinSecurity.ProfileScan.ScanOnFirstMessage = scanOnFirstMessage;

        await configService.SaveWelcomeAsync(chat, welcome, Actor.SystemSeed);

        // Confirm the flags actually landed — silently-unpersisted flags would make the
        // positive test fail for the wrong reason and the negative test pass vacuously.
        var effective = await configService.GetEffectiveWelcomeAsync(MainChatId);
        var profileScan = effective?.JoinSecurity.ProfileScan;
        Assert.That(profileScan, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(profileScan!.Enabled, Is.True);
            Assert.That(profileScan.ScanOnFirstMessage, Is.EqualTo(scanOnFirstMessage));
            Assert.That(profileScan.ScanOnJoin, Is.EqualTo(scanOnOtherTriggers));
            Assert.That(profileScan.ScanOnProfileChange, Is.EqualTo(scanOnOtherTriggers));
        }
    }

    // Forward-progress control for the negative test. The message row is written at
    // MessageProcessingService.cs:556, well before the first-message gate call, so its
    // presence proves the SUT was not killed by the catch-all somewhere upstream of the
    // gate decision. Read back through the real IMessageHistoryRepository, whose
    // GetMessageAsync uses only LEFT joins and applies no deleted-row filter, so it cannot
    // return null for a row that exists.
    private async Task AssertMessageWasPersistedAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var messageRepository = scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>();

        var persisted = await messageRepository.GetMessageAsync(TestMessageId, MainChatId);
        Assert.That(persisted, Is.Not.Null,
            "The message was never persisted, so HandleNewMessageAsync failed upstream of the "
            + "profile-scan gate. The DidNotReceive assertion below would pass vacuously.");
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
