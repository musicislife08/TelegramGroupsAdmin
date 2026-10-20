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
using TelegramGroupsAdmin.Core.Imaging;
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
using TelegramGroupsAdmin.Telegram.Services.Identity;
using TelegramGroupsAdmin.Telegram.Services.Moderation;
using TelegramGroupsAdmin.Telegram.Services.UserApi;

namespace TelegramGroupsAdmin.IntegrationTests.Telegram;

/// <summary>
/// Pins the rename-on-a-new-message regression through the real
/// <see cref="MessageProcessingService.HandleNewMessageAsync"/>. The sender is observed before the
/// message row exists, so the rename's <c>ProfileChange</c> audit must not reference the message:
/// before, the composite foreign key rejected the row and the whole observation rolled back,
/// losing the new names, the history row and the audit. Harness as in
/// <see cref="MessageProcessingFirstMessageScanTests"/>; the substituted scan returns Banned so
/// content detection is short-circuited.
/// </summary>
[TestFixture]
public class MessageProcessingRenameTests
{
    private const long MainChatId = GoldenDatasetConstants.Chats.MainChatId;
    private const long UserId = GoldenDatasetConstants.IdentityService.UntrustedNoHistoryUserId;
    private const int TextMessageId = 990101;
    private const int ServiceMessageId = 990102;

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

        _dataPath = Path.Combine(Path.GetTempPath(), $"tga-rename-{Guid.NewGuid():N}");
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

        // ── Real: the identity service and the audit repository it writes through ──
        services.AddScoped<IUserIdentityService, UserIdentityService>();
        services.AddScoped<IUserActionsRepository, UserActionsRepository>();

        // ── Real: concrete handlers on the pre-detection path. All take interface-only
        //    dependencies and short-circuit for a plain-text, non-command message. ──
        services.AddSingleton<CommandRouter>();
        services.AddScoped<AdminMentionHandler>();
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();
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
    public async Task NewMessage_FromRenamedUser_RecordsRenameAndStoresMessage()
    {
        var before = await ReadStateAsync();

        await _sut.HandleNewMessageAsync(CreateTextMessage("Renamed Sender"), CancellationToken.None);

        await AssertRenameRecordedAsync(before, "Renamed Sender");
        using var scope = _serviceProvider.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IMessageHistoryRepository>()
            .GetMessageAsync(TextMessageId, MainChatId);
        Assert.That(stored, Is.Not.Null, "The renamed sender's message was not stored.");
    }

    [Test]
    public async Task ServiceMessage_FromRenamedUser_RecordsRename()
    {
        var before = await ReadStateAsync();

        await _sut.HandleNewMessageAsync(CreateJoinMessage("Renamed Joiner"), CancellationToken.None);

        await AssertRenameRecordedAsync(before, "Renamed Joiner");
    }

    private sealed record State(string? FirstName, int HistoryCount, int ProfileChangeCount);

    private async Task<State> ReadStateAsync()
    {
        await using var context = _testHelper.GetDbContext();
        var user = await context.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        var history = await context.UsernameHistory.CountAsync(h => h.UserId == UserId);
        var audits = await context.UserActions.CountAsync(a =>
            a.UserId == UserId && a.ActionType == (int)UserActionType.ProfileChange);
        Assert.That(user.FirstName, Is.Not.Null.And.Not.EqualTo("Renamed Sender").And.Not.EqualTo("Renamed Joiner"),
            "Precondition: the anchor must not already carry the new name.");
        return new State(user.FirstName, history, audits);
    }

    private async Task AssertRenameRecordedAsync(State before, string newFirstName)
    {
        await using var context = _testHelper.GetDbContext();
        var user = await context.TelegramUsers.AsNoTracking().SingleAsync(u => u.TelegramUserId == UserId);
        var history = await context.UsernameHistory.AsNoTracking()
            .Where(h => h.UserId == UserId).OrderByDescending(h => h.Id).ToListAsync();
        var audits = await context.UserActions.AsNoTracking()
            .Where(a => a.UserId == UserId && a.ActionType == (int)UserActionType.ProfileChange).ToListAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(user.FirstName, Is.EqualTo(newFirstName));
            Assert.That(history, Has.Count.EqualTo(before.HistoryCount + 1));
            Assert.That(history[0].FirstName, Is.EqualTo(before.FirstName));
            Assert.That(audits, Has.Count.EqualTo(before.ProfileChangeCount + 1));
            var audit = audits.OrderByDescending(a => a.Id).First();
            Assert.That(audit.ChatId, Is.EqualTo(MainChatId));
            Assert.That(audit.MessageId, Is.Null);
        }
    }

    private static Message CreateTextMessage(string firstName) => new()
    {
        Id = TextMessageId,
        Date = DateTime.UtcNow,
        Chat = TelegramTestFactory.CreateChat(MainChatId, ChatType.Supergroup, "Main Community"),
        From = TelegramTestFactory.CreateUser(UserId, firstName, username: "pastramiherbs"),
        Text = "hi"
    };

    private static Message CreateJoinMessage(string firstName)
    {
        var from = TelegramTestFactory.CreateUser(UserId, firstName, username: "pastramiherbs");
        return new Message
        {
            Id = ServiceMessageId,
            Date = DateTime.UtcNow,
            Chat = TelegramTestFactory.CreateChat(MainChatId, ChatType.Supergroup, "Main Community"),
            From = from,
            NewChatMembers = [from]
        };
    }

    private static ProfileScanResult BannedScanResult() => new(
        TelegramUserId: UserId,
        Bio: null,
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
        AiReason: "test",
        AiSignalsDetected: []);
}
